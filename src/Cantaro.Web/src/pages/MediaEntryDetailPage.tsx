import { useState, useEffect, useCallback, useRef } from 'react';
import DOMPurify from 'dompurify';
import { GlassCard, GradientButton } from '../components/ui/GlassComponents';
import { mediaApi } from '../services/mediaApi';
import { mainMediaProviderId, mediaProviderCatalog } from '../services/mediaProviders';
import {
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../services/mediaRefreshCache';
import type {
  MediaLibraryEntryDetailDto,
  MediaProviderAvailabilityLinkDto,
  MediaProviderLinkSummaryDto,
  MediaProviderSearchResultDto,
  MediaLinkConflictDto,
} from '../services/mediaApi';

interface ProviderAvailabilityState {
  status: 'loading' | 'loaded' | 'error';
  links: MediaProviderAvailabilityLinkDto[];
  error?: string;
}

// ── Label helpers ──────────────────────────────────────────────────────────────

function mediaKindLabel(kind: string): string {
  const map: Record<string, string> = {
    anime: 'Anime',
    manga: 'Manga',
    lightNovel: 'Light Novel',
    oneShot: 'One Shot',
    novel: 'Novel',
  };
  return map[kind] ?? kind;
}

function releaseStatusLabel(dimension: string): string {
  const map: Record<string, string> = {
    airing: 'Currently Airing',
    finished: 'Finished',
    notYetAired: 'Not Yet Aired',
    not_yet_aired: 'Not Yet Aired',
    cancelled: 'Cancelled',
    hiatus: 'On Hiatus',
    unknown: 'Unknown',
  };
  return map[dimension] ?? dimension;
}

function releaseStatusColor(dimension: string): string {
  switch (dimension) {
    case 'airing': return 'bg-emerald-100 text-emerald-800';
    case 'finished': return 'bg-blue-100 text-blue-800';
    case 'notYetAired':
    case 'not_yet_aired': return 'bg-violet-100 text-violet-800';
    case 'cancelled': return 'bg-rose-100 text-rose-800';
    case 'hiatus': return 'bg-amber-100 text-amber-800';
    default: return 'bg-gray-100 text-gray-600';
  }
}

function nextReleaseDisplay(timestamp?: string): { relative: string; absolute: string } | null {
  if (!timestamp) {
    return null;
  }

  const parsed = Date.parse(timestamp);
  if (Number.isNaN(parsed)) {
    return null;
  }

  const diffMs = parsed - Date.now();
  const minute = 60 * 1000;
  const hour = 60 * minute;
  const day = 24 * hour;
  const week = 7 * day;
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

  let relative: string;
  if (Math.abs(diffMs) < hour) {
    relative = formatter.format(Math.round(diffMs / minute), 'minute');
  } else if (Math.abs(diffMs) < day) {
    relative = formatter.format(Math.round(diffMs / hour), 'hour');
  } else if (Math.abs(diffMs) < week) {
    relative = formatter.format(Math.round(diffMs / day), 'day');
  } else {
    relative = new Intl.DateTimeFormat(undefined, {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    }).format(parsed);
  }

  const absolute = new Intl.DateTimeFormat(undefined, {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(parsed);

  return { relative, absolute };
}

function providerAvailabilityKey(provider: string, externalId: string): string {
  return `${provider}:${externalId}`;
}

const NORMALIZED_STATUSES = [
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'planning', label: 'Planning' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
  { value: 'repeating', label: 'Rewatching / Rereading' },
];

// ── Artwork component ──────────────────────────────────────────────────────────

interface ArtworkProps {
  posterUrl?: string;
  title: string;
}

interface SanitizedSynopsisProps {
  html: string;
  className?: string;
}

function SanitizedSynopsis({ html, className }: SanitizedSynopsisProps) {
  const sanitizedHtml = DOMPurify.sanitize(html, {
    ALLOWED_TAGS: ['br', 'i', 'em', 'b', 'strong'],
    ALLOWED_ATTR: [],
  });

  return <div className={className} dangerouslySetInnerHTML={{ __html: sanitizedHtml }} />;
}

function Artwork({ posterUrl, title }: ArtworkProps) {
  const [failed, setFailed] = useState(false);

  if (!posterUrl || failed) {
    return (
      <div className="flex h-full w-full items-center justify-center rounded-2xl bg-linear-to-br from-indigo-100 to-purple-100">
        <span className="text-4xl" aria-hidden>🎌</span>
        <span className="sr-only">{title} — no artwork available</span>
      </div>
    );
  }

  return (
    <img
      src={posterUrl}
      alt={`${title} cover art`}
      className="h-full w-full rounded-2xl object-cover"
      onError={() => setFailed(true)}
    />
  );
}

// ── Number input for progress ──────────────────────────────────────────────────

interface ProgressFieldProps {
  label: string;
  value: number | undefined;
  max?: number;
  supported: boolean;
  onChange: (value: number) => void;
}

function ProgressField({ label, value, max, supported, onChange }: ProgressFieldProps) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(String(value ?? 0));
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (editing) inputRef.current?.select();
  }, [editing]);

  if (!supported) {
    return (
      <div className="rounded-xl bg-gray-50 px-4 py-3">
        <p className="text-xs font-medium text-gray-400 uppercase tracking-wide">{label}</p>
        <p className="mt-1 text-sm text-gray-400 italic">Not tracked for this type</p>
      </div>
    );
  }

  return (
    <div className="rounded-xl bg-white/70 px-4 py-3">
      <p className="text-xs font-medium text-gray-500 uppercase tracking-wide">{label}</p>
      <div className="mt-1 flex items-center gap-2">
        {editing ? (
          <>
            <input
              ref={inputRef}
              type="number"
              min={0}
              max={max}
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              className="w-20 rounded-lg border border-indigo-300 bg-white px-2 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  const num = parseInt(draft, 10);
                  if (!isNaN(num) && num >= 0) {
                    onChange(num);
                  }
                  setEditing(false);
                }
                if (e.key === 'Escape') {
                  setDraft(String(value ?? 0));
                  setEditing(false);
                }
              }}
            />
            {max ? <span className="text-xs text-gray-400">/ {max}</span> : null}
            <button
              type="button"
              className="text-xs text-indigo-600 hover:underline"
              onClick={() => {
                const num = parseInt(draft, 10);
                if (!isNaN(num) && num >= 0) onChange(num);
                setEditing(false);
              }}
            >
              Save
            </button>
            <button
              type="button"
              className="text-xs text-gray-400 hover:underline"
              onClick={() => {
                setDraft(String(value ?? 0));
                setEditing(false);
              }}
            >
              Cancel
            </button>
          </>
        ) : (
          <>
            <span className="text-xl font-semibold text-gray-800">{value ?? 0}</span>
            {max ? <span className="text-sm text-gray-400">/ {max}</span> : null}
            <button
              type="button"
              className="ml-1 text-xs text-indigo-500 hover:underline"
              onClick={() => {
                setDraft(String(value ?? 0));
                setEditing(true);
              }}
            >
              Edit
            </button>
          </>
        )}
      </div>
    </div>
  );
}

// ── Manual search / link dialog ────────────────────────────────────────────────

interface SearchLinkDialogProps {
  libraryEntryId: string;
  mediaKind: string;
  existingLinks: MediaProviderLinkSummaryDto[];
  onClose: () => void;
  onLinked: () => void;
}

function SearchLinkDialog({ libraryEntryId, mediaKind, existingLinks, onClose, onLinked }: SearchLinkDialogProps) {
  const [providerId, setProviderId] = useState(mediaProviderCatalog[0]?.id ?? '');
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<MediaProviderSearchResultDto[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [linkingId, setLinkingId] = useState<string | null>(null);
  const [conflict, setConflict] = useState<{ providerMediaId: string; conflictInfo: MediaLinkConflictDto } | null>(null);
  const [linkError, setLinkError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const handleSearch = useCallback(async () => {
    if (!query.trim()) return;
    setIsSearching(true);
    setSearchError(null);
    setResults([]);
    setConflict(null);
    setLinkError(null);
    try {
      const data = await mediaApi.searchProvider(providerId, {
        query: query.trim(),
        mediaKinds: mediaKind ? [mediaKind] : undefined,
        limit: 20,
      });
      setResults(data);
    } catch (err) {
      setSearchError(err instanceof Error ? err.message : 'Search failed');
    } finally {
      setIsSearching(false);
    }
  }, [query, providerId, mediaKind]);

  const handleLink = async (providerMediaId: string, forceRelink = false) => {
    setLinkingId(providerMediaId);
    setConflict(null);
    setLinkError(null);
    try {
      await mediaApi.linkProvider(libraryEntryId, { providerId, providerMediaId, forceRelink });
      onLinked();
    } catch (err) {
      if (err instanceof Error) {
        // Try parsing conflict JSON from error message
        try {
          const parsed = JSON.parse(err.message) as MediaLinkConflictDto;
          setConflict({ providerMediaId, conflictInfo: parsed });
        } catch {
          setLinkError(err.message);
        }
      } else {
        setLinkError('Failed to link provider');
      }
    } finally {
      setLinkingId(null);
    }
  };

  const alreadyLinkedIds = new Set(existingLinks.filter((l) => l.provider === providerId).map((l) => l.externalId));

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4"
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div className="w-full max-w-2xl max-h-[90vh] flex flex-col rounded-3xl border border-white/80 bg-white/90 shadow-2xl backdrop-blur">
        <div className="flex items-center justify-between border-b border-gray-100 px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">Search &amp; link a provider entry</h2>
          <button
            type="button"
            className="rounded-xl px-3 py-1.5 text-sm text-gray-500 hover:bg-gray-100 transition"
            onClick={onClose}
            aria-label="Close dialog"
          >
            ✕
          </button>
        </div>

        <div className="px-6 pt-4 pb-3 space-y-3">
          {mediaProviderCatalog.length > 1 ? (
            <div className="flex gap-2">
              {mediaProviderCatalog.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => { setProviderId(p.id); setResults([]); setSearchError(null); }}
                  className={`rounded-xl px-4 py-2 text-sm font-medium transition ${providerId === p.id
                    ? 'bg-indigo-500 text-white'
                    : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                    }`}
                >
                  {p.icon} {p.name}
                </button>
              ))}
            </div>
          ) : null}

          <div className="flex gap-2">
            <input
              ref={inputRef}
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void handleSearch(); }}
              placeholder="Search by title…"
              className="flex-1 rounded-xl border border-gray-200 bg-white px-4 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
            />
            <GradientButton
              gradient="from-indigo-500 to-purple-500"
              onClick={() => void handleSearch()}
              disabled={isSearching || !query.trim()}
              aria-busy={isSearching}
            >
              {isSearching ? 'Searching…' : 'Search'}
            </GradientButton>
          </div>

          {searchError ? (
            <p className="rounded-xl bg-rose-50 px-4 py-2 text-sm text-rose-700">{searchError}</p>
          ) : null}
          {linkError ? (
            <p className="rounded-xl bg-rose-50 px-4 py-2 text-sm text-rose-700">{linkError}</p>
          ) : null}
          {conflict ? (
            <div className="rounded-xl bg-amber-50 px-4 py-3 text-sm">
              <p className="font-semibold text-amber-800">This entry is already linked to another title</p>
              <p className="mt-1 text-amber-700">
                Currently linked to: <span className="font-medium">{conflict.conflictInfo.conflictingCanonicalTitle}</span>
              </p>
              <div className="mt-2 flex gap-2">
                <GradientButton
                  gradient="from-amber-500 to-orange-500"
                  onClick={() => void handleLink(conflict.providerMediaId, true)}
                  disabled={!!linkingId}
                >
                  Force relink
                </GradientButton>
                <GradientButton tone="soft" onClick={() => setConflict(null)}>
                  Cancel
                </GradientButton>
              </div>
            </div>
          ) : null}
        </div>

        <div className="flex-1 overflow-y-auto px-6 pb-6 space-y-2">
          {results.length === 0 && !isSearching ? (
            <p className="text-center text-sm text-gray-400 py-8">
              {query.trim() ? 'No results found.' : 'Enter a title to search.'}
            </p>
          ) : (
            results.map((result) => {
              const isLinked = alreadyLinkedIds.has(result.providerMediaId);
              const isLinking = linkingId === result.providerMediaId;
              return (
                <div
                  key={result.providerMediaId}
                  className="flex gap-3 rounded-2xl border border-gray-100 bg-white/70 p-3 items-start"
                >
                  <div className="shrink-0 h-16 w-12 overflow-hidden rounded-lg bg-gray-100">
                    {result.posterUrl ? (
                      <img
                        src={result.posterUrl}
                        alt={`${result.title} artwork`}
                        className="h-full w-full object-cover"
                        onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                      />
                    ) : (
                      <div className="h-full w-full flex items-center justify-center text-xl">🎌</div>
                    )}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="font-medium text-gray-900 leading-tight">{result.title}</p>
                    {result.nativeTitle ? (
                      <p className="text-xs text-gray-500 truncate">{result.nativeTitle}</p>
                    ) : null}
                    <div className="mt-1 flex flex-wrap items-center gap-1.5">
                      <span className="rounded-full bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700">
                        {mediaKindLabel(result.mediaKind)}
                      </span>
                      {result.startYear ? (
                        <span className="text-xs text-gray-400">{result.startYear}</span>
                      ) : null}
                      {result.episodeCount ? (
                        <span className="text-xs text-gray-400">{result.episodeCount} ep</span>
                      ) : null}
                      {result.chapterCount ? (
                        <span className="text-xs text-gray-400">{result.chapterCount} ch</span>
                      ) : null}
                    </div>
                    {result.synopsis ? (
                      <SanitizedSynopsis
                        html={result.synopsis}
                        className="mt-1 line-clamp-2 text-xs leading-relaxed text-gray-500"
                      />
                    ) : null}
                  </div>
                  <div className="shrink-0">
                    {isLinked ? (
                      <span className="rounded-full bg-green-100 px-3 py-1 text-xs font-semibold text-green-800">
                        Linked
                      </span>
                    ) : (
                      <GradientButton
                        gradient="from-indigo-500 to-purple-500"
                        className="text-xs px-3 py-2"
                        disabled={!!linkingId}
                        aria-busy={isLinking}
                        onClick={() => void handleLink(result.providerMediaId)}
                      >
                        {isLinking ? '…' : 'Link'}
                      </GradientButton>
                    )}
                  </div>
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}

// ── Entry detail page ──────────────────────────────────────────────────────────

interface MediaEntryDetailPageProps {
  libraryEntryId: string;
  onNavigateBack: () => void;
}

export function MediaEntryDetailPage({ libraryEntryId, onNavigateBack }: MediaEntryDetailPageProps) {
  const [entry, setEntry] = useState<MediaLibraryEntryDetailDto | null>(null);
  const [availabilityByProviderLink, setAvailabilityByProviderLink] = useState<Record<string, ProviderAvailabilityState>>({});
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshingRemote, setIsRefreshingRemote] = useState(false);
  const [isSavingProgress, setIsSavingProgress] = useState(false);
  const [isSavingStatus, setIsSavingStatus] = useState(false);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [showLinkDialog, setShowLinkDialog] = useState(false);
  const [unlinkingId, setUnlinkingId] = useState<string | null>(null);

  // Local progress drafts — synced from entry
  const [progressEpisodes, setProgressEpisodes] = useState<number | undefined>();
  const [progressChapters, setProgressChapters] = useState<number | undefined>();
  const [progressVolumes, setProgressVolumes] = useState<number | undefined>();
  const [selectedStatus, setSelectedStatus] = useState('');

  const loadEntry = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await mediaApi.getLibraryEntry(libraryEntryId);
      setEntry(data);
      setProgressEpisodes(data.progressEpisodes);
      setProgressChapters(data.progressChapters);
      setProgressVolumes(data.progressVolumes);
      setSelectedStatus(data.normalizedStatus);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load entry');
    } finally {
      setIsLoading(false);
    }
  }, [libraryEntryId]);

  useEffect(() => {
    void loadEntry();
  }, [loadEntry]);

  useEffect(() => {
    if (!entry || entry.providerLinks.length === 0) {
      setAvailabilityByProviderLink({});
      return;
    }

    const providerLinks = entry.providerLinks;
    let isCancelled = false;

    setAvailabilityByProviderLink((current) => {
      const next: Record<string, ProviderAvailabilityState> = {};
      for (const link of providerLinks) {
        const key = providerAvailabilityKey(link.provider, link.externalId);
        next[key] = current[key] ?? { status: 'loading', links: [] };
      }

      return next;
    });

    const loadAvailability = async () => {
      const results = await Promise.all(providerLinks.map(async (link) => {
        const key = providerAvailabilityKey(link.provider, link.externalId);

        try {
          const details = await mediaApi.getTitleDetails(link.provider, link.externalId);
          return {
            key,
            state: {
              status: 'loaded' as const,
              links: details.availabilityLinks ?? [],
            },
          };
        } catch (err) {
          return {
            key,
            state: {
              status: 'error' as const,
              links: [],
              error: err instanceof Error ? err.message : 'Failed to load availability',
            },
          };
        }
      }));

      if (isCancelled) {
        return;
      }

      setAvailabilityByProviderLink((current) => {
        const next = { ...current };
        for (const result of results) {
          next[result.key] = result.state;
        }

        return next;
      });
    };

    void loadAvailability();

    return () => {
      isCancelled = true;
    };
  }, [entry]);

  useEffect(() => {
    if (!entry || !entry.isConnected || isRefreshingRemote) {
      return;
    }

    const refreshProviderId = entry.provider || mainMediaProviderId;
    if (!isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(refreshProviderId)))) {
      return;
    }

    let isCancelled = false;

    const refreshFromRemote = async () => {
      setIsRefreshingRemote(true);
      try {
        const result = await mediaApi.importLibrary(refreshProviderId);
        if (isCancelled) {
          return;
        }

        writeStoredValue(remoteCheckTimestampKey(refreshProviderId), result.importedAt);
        await loadEntry();
      } catch (err) {
        if (!isCancelled) {
          setSaveMessage(err instanceof Error ? `Error: ${err.message}` : 'Error: Failed to refresh entry');
        }
      } finally {
        if (!isCancelled) {
          setIsRefreshingRemote(false);
        }
      }
    };

    void refreshFromRemote();

    return () => {
      isCancelled = true;
    };
  }, [entry, isRefreshingRemote, loadEntry]);

  const showSaveConfirmation = (message: string) => {
    setSaveMessage(message);
    setTimeout(() => setSaveMessage(null), 3000);
  };

  const handleSaveProgress = async () => {
    if (!entry) return;
    setIsSavingProgress(true);
    try {
      await mediaApi.updateProgress(libraryEntryId, {
        progressEpisodes,
        progressChapters,
        progressVolumes,
      });
      setEntry((prev) => prev ? { ...prev, progressEpisodes, progressChapters, progressVolumes } : prev);
      showSaveConfirmation('Progress saved');
    } catch (err) {
      setSaveMessage(`Error: ${err instanceof Error ? err.message : 'Failed to save'}`);
    } finally {
      setIsSavingProgress(false);
    }
  };

  const handleSaveStatus = async () => {
    if (!entry) return;
    setIsSavingStatus(true);
    try {
      await mediaApi.updateStatus(libraryEntryId, { status: selectedStatus });
      setEntry((prev) => prev ? { ...prev, normalizedStatus: selectedStatus } : prev);
      showSaveConfirmation('Status saved');
    } catch (err) {
      setSaveMessage(`Error: ${err instanceof Error ? err.message : 'Failed to save'}`);
    } finally {
      setIsSavingStatus(false);
    }
  };

  const handleToggleAutoProgress = async () => {
    if (!entry) return;
    const newValue = !entry.autoProgressFromObservations;
    try {
      await mediaApi.updateAutoProgress(libraryEntryId, { enabled: newValue });
      setEntry((prev) => prev ? { ...prev, autoProgressFromObservations: newValue } : prev);
      showSaveConfirmation(newValue ? 'Auto-progress enabled' : 'Auto-progress disabled');
    } catch (err) {
      setSaveMessage(`Error: ${err instanceof Error ? err.message : 'Failed to update'}`);
    }
  };

  const handleUnlink = async (providerId: string) => {
    setUnlinkingId(providerId);
    try {
      await mediaApi.unlinkProvider(libraryEntryId, providerId);
      setEntry((prev) =>
        prev
          ? { ...prev, providerLinks: prev.providerLinks.filter((l) => l.provider !== providerId) }
          : prev,
      );
    } catch (err) {
      setSaveMessage(`Error: ${err instanceof Error ? err.message : 'Failed to unlink'}`);
    } finally {
      setUnlinkingId(null);
    }
  };

  // Provider links don't expose posterUrl yet — Artwork degrades gracefully

  if (isLoading) {
    return (
      <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50">
        <div className="relative z-10 mx-auto max-w-4xl px-6 pt-8 pb-16">
          <GlassCard className="h-96 animate-pulse" />
        </div>
      </div>
    );
  }

  if (error || !entry) {
    return (
      <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50">
        <div className="relative z-10 mx-auto max-w-4xl px-6 pt-8 pb-16 space-y-4">
          <GradientButton tone="soft" onClick={onNavigateBack}>← Back to library</GradientButton>
          <GlassCard className="p-6">
            <p className="text-rose-700">{error ?? 'Entry not found'}</p>
            <div className="mt-3">
              <GradientButton tone="soft" onClick={() => void loadEntry()}>Retry</GradientButton>
            </div>
          </GlassCard>
        </div>
      </div>
    );
  }

  const { title } = entry;
  const dim = title.primaryProgressDimension;
  const supportsEpisodes = dim === 'episode';
  const supportsChapters = dim === 'chapter';
  const supportsVolumes = dim === 'volume' || dim === 'chapter'; // manga often tracks both

  const hasProgressChanged =
    progressEpisodes !== entry.progressEpisodes ||
    progressChapters !== entry.progressChapters ||
    progressVolumes !== entry.progressVolumes;
  const hasStatusChanged = selectedStatus !== entry.normalizedStatus;
  const nextRelease = nextReleaseDisplay(entry.nextReleaseAt);

  return (
    <>
      <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
        <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
        <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

        <div className="relative z-10 mx-auto max-w-4xl space-y-6 px-6 pt-8 pb-16">
          {/* Header */}
          <header className="flex flex-wrap items-center gap-3">
            <GradientButton tone="soft" onClick={onNavigateBack}>
              ← Library
            </GradientButton>
            <div className="flex flex-wrap gap-2 items-center">
              <span className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-700">
                {mediaKindLabel(title.mediaKind)}
              </span>
              <span className={`rounded-full px-3 py-1 text-xs font-semibold ${entry.isConnected ? 'bg-green-100 text-green-800' : 'bg-amber-100 text-amber-800'
                }`}>
                {entry.isConnected ? 'Synced' : 'Not synced'}
              </span>
            </div>
          </header>

          {/* Save confirmation */}
          {saveMessage ? (
            <div className={`rounded-2xl px-4 py-3 text-sm font-medium ${saveMessage.startsWith('Error:') ? 'bg-rose-50 text-rose-700' : 'bg-emerald-50 text-emerald-800'
              }`}>
              {saveMessage}
            </div>
          ) : null}

          {isRefreshingRemote ? (
            <div className="rounded-2xl bg-sky-50 px-4 py-3 text-sm font-medium text-sky-700">
              Refreshing provider data…
            </div>
          ) : null}

          {/* Main content: artwork + core metadata */}
          <GlassCard className="overflow-visible">
            <div className="flex flex-col gap-6 p-6 sm:flex-row">
              {/* Artwork */}
              <div className="h-48 w-32 shrink-0 overflow-hidden rounded-2xl sm:h-56 sm:w-40">
                <Artwork posterUrl={title.posterUrl} title={title.canonicalTitle} />
              </div>

              {/* Core metadata */}
              <div className="min-w-0 flex-1 space-y-3">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div className="min-w-0">
                    <h1 className="text-2xl font-bold leading-tight text-gray-900">{title.canonicalTitle}</h1>
                    {title.originalTitle && title.originalTitle !== title.canonicalTitle ? (
                      <p className="mt-1 text-sm text-gray-500">{title.originalTitle}</p>
                    ) : null}
                  </div>

                  {nextRelease ? (
                    <div className="shrink-0 self-start rounded-2xl border border-cyan-100 bg-cyan-50 px-4 py-3 text-right shadow-sm">
                      <p className="text-[11px] font-semibold tracking-[0.18em] text-cyan-700 uppercase">
                        {entry.nextReleaseLabel ?? 'Next release'}
                      </p>
                      <p className="mt-1 text-sm font-semibold text-slate-900">{nextRelease.relative}</p>
                      <p className="mt-1 text-xs text-slate-500">{nextRelease.absolute}</p>
                    </div>
                  ) : null}
                </div>

                <div className="flex flex-wrap gap-2 items-center">
                  {title.startYear ? (
                    <span className="rounded-full bg-gray-100 px-3 py-1 text-xs text-gray-600">
                      {title.startYear}
                    </span>
                  ) : null}
                  <span className={`rounded-full px-3 py-1 text-xs font-semibold ${releaseStatusColor(title.releaseStatusDimension)}`}>
                    {releaseStatusLabel(title.releaseStatusDimension)}
                  </span>
                </div>

                {/* Counts */}
                <div className="flex flex-wrap gap-3">
                  {title.episodeCount ? (
                    <span className="text-sm text-gray-600">{title.episodeCount} episodes</span>
                  ) : null}
                  {title.chapterCount ? (
                    <span className="text-sm text-gray-600">{title.chapterCount} chapters</span>
                  ) : null}
                  {title.volumeCount ? (
                    <span className="text-sm text-gray-600">{title.volumeCount} volumes</span>
                  ) : null}
                  {!title.episodeCount && !title.chapterCount && !title.volumeCount ? (
                    <span className="text-sm text-gray-400 italic">Count unknown</span>
                  ) : null}
                </div>

                {title.synopsis ? (
                  <SanitizedSynopsis
                    html={title.synopsis}
                    className="text-sm leading-relaxed text-gray-600"
                  />
                ) : null}

                {/* Raw provider status */}
                {entry.rawStatus || entry.rawListName ? (
                  <p className="text-xs text-gray-400">
                    Provider status: {entry.rawListName ?? entry.rawStatus}
                  </p>
                ) : null}
              </div>
            </div>
          </GlassCard>

          {/* Status editor */}
          <GlassCard className="p-6">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-gray-500">Status</h2>
            <div className="mt-3 flex flex-wrap items-center gap-3">
              <select
                className="rounded-xl border border-gray-200 bg-white/80 px-4 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400"
                value={selectedStatus}
                onChange={(e) => setSelectedStatus(e.target.value)}
              >
                {NORMALIZED_STATUSES.map((s) => (
                  <option key={s.value} value={s.value}>{s.label}</option>
                ))}
              </select>
              {hasStatusChanged ? (
                <GradientButton
                  gradient="from-indigo-500 to-purple-500"
                  onClick={() => void handleSaveStatus()}
                  disabled={isSavingStatus}
                  aria-busy={isSavingStatus}
                >
                  {isSavingStatus ? 'Saving…' : 'Save status'}
                </GradientButton>
              ) : null}
              <span className="text-xs text-gray-400">
                Last updated {new Date(entry.updatedAt).toLocaleDateString()}
              </span>
            </div>
          </GlassCard>

          {/* Progress editor */}
          <GlassCard className="p-6">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-gray-500">Progress</h2>
            <div className="mt-3 grid gap-3 sm:grid-cols-3">
              <ProgressField
                label="Episodes"
                value={progressEpisodes}
                max={title.episodeCount}
                supported={supportsEpisodes}
                onChange={(v) => setProgressEpisodes(v)}
              />
              <ProgressField
                label="Chapters"
                value={progressChapters}
                max={title.chapterCount}
                supported={supportsChapters}
                onChange={(v) => setProgressChapters(v)}
              />
              <ProgressField
                label="Volumes"
                value={progressVolumes}
                max={title.volumeCount}
                supported={supportsVolumes}
                onChange={(v) => setProgressVolumes(v)}
              />
            </div>
            {hasProgressChanged ? (
              <div className="mt-4">
                <GradientButton
                  gradient="from-indigo-500 to-purple-500"
                  onClick={() => void handleSaveProgress()}
                  disabled={isSavingProgress}
                  aria-busy={isSavingProgress}
                >
                  {isSavingProgress ? 'Saving…' : 'Save progress'}
                </GradientButton>
              </div>
            ) : null}
            <p className="mt-3 text-xs text-gray-400">
              Editing progress here updates Cantaro only. Changes are not automatically pushed to your connected provider.
            </p>
          </GlassCard>

          {/* Auto-progress */}
          <GlassCard className="p-6">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-gray-500">Auto-progress</h2>
            <div className="mt-3 flex items-start gap-4">
              <button
                type="button"
                role="switch"
                aria-checked={entry.autoProgressFromObservations}
                onClick={() => void handleToggleAutoProgress()}
                className={`relative mt-0.5 inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-400 focus:ring-offset-2 ${entry.autoProgressFromObservations ? 'bg-indigo-500' : 'bg-gray-200'
                  }`}
              >
                <span
                  aria-hidden
                  className={`inline-block h-5 w-5 transform rounded-full bg-white shadow transition-transform ${entry.autoProgressFromObservations ? 'translate-x-5' : 'translate-x-0'
                    }`}
                />
              </button>
              <div>
                <p className="text-sm font-medium text-gray-800">
                  {entry.autoProgressFromObservations ? 'Enabled' : 'Disabled'}
                </p>
                <p className="mt-1 text-xs text-gray-500">
                  When enabled, Cantaro may advance your progress counter when the browser extension
                  detects you watching an episode. Progress only moves forward and is subject to
                  backend matching confidence — it will not overwrite remote changes.
                </p>
              </div>
            </div>
          </GlassCard>

          {/* Provider links */}
          <GlassCard className="p-6">
            <div className="flex items-center justify-between gap-3">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-gray-500">Provider links</h2>
              <GradientButton
                gradient="from-blue-500 to-cyan-500"
                onClick={() => setShowLinkDialog(true)}
              >
                + Link provider
              </GradientButton>
            </div>

            {entry.providerLinks.length === 0 ? (
              <p className="mt-4 text-sm text-gray-400 italic">
                No provider links. Use "Link provider" to connect this entry to an external service.
              </p>
            ) : (
              <ul className="mt-4 space-y-2">
                {entry.providerLinks.map((link) => {
                  const catalog = mediaProviderCatalog.find((p) => p.id === link.provider);
                  const availability = availabilityByProviderLink[providerAvailabilityKey(link.provider, link.externalId)];
                  return (
                    <li key={link.id} className="flex items-center justify-between gap-3 rounded-xl bg-white/70 px-4 py-3">
                      <div className="flex items-center gap-3 min-w-0">
                        {catalog ? (
                          <span className="text-lg" aria-hidden>{catalog.icon}</span>
                        ) : null}
                        <div className="min-w-0">
                          <p className="text-sm font-medium text-gray-800">
                            {catalog?.name ?? link.provider}
                          </p>
                          <p className="text-xs text-gray-500">
                            ID: {link.externalId}
                            {link.linkSource !== 'manual' ? null : ' · manual'}
                          </p>
                          {availability?.status === 'loading' ? (
                            <p className="mt-1 text-xs text-gray-400">Loading availability…</p>
                          ) : null}
                          {availability?.status === 'error' ? (
                            <p className="mt-1 text-xs text-gray-400" title={availability.error}>
                              Availability unavailable
                            </p>
                          ) : null}
                          {availability?.status === 'loaded' && availability.links.length > 0 ? (
                            <div className="mt-2 flex flex-wrap gap-2">
                              {availability.links.map((availabilityLink) => {
                                const chipClasses = 'inline-flex items-center rounded-full bg-sky-50 px-2.5 py-1 text-xs font-medium text-sky-700 hover:bg-sky-100';

                                return availabilityLink.url ? (
                                  <a
                                    key={`${link.id}:${availabilityLink.serviceId}`}
                                    href={availabilityLink.url}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className={chipClasses}
                                    title={availabilityLink.notes ?? availabilityLink.availabilityKind}
                                  >
                                    {availabilityLink.displayName} ↗
                                  </a>
                                ) : (
                                  <span
                                    key={`${link.id}:${availabilityLink.serviceId}`}
                                    className={chipClasses}
                                    title={availabilityLink.notes ?? availabilityLink.availabilityKind}
                                  >
                                    {availabilityLink.displayName}
                                  </span>
                                );
                              })}
                            </div>
                          ) : null}
                        </div>
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        {link.externalUrl ? (
                          <a
                            href={link.externalUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-xs text-indigo-500 hover:underline"
                          >
                            Open ↗
                          </a>
                        ) : null}
                        <button
                          type="button"
                          className="rounded-lg px-3 py-1.5 text-xs text-rose-600 hover:bg-rose-50 transition disabled:opacity-50"
                          disabled={unlinkingId === link.provider}
                          onClick={() => void handleUnlink(link.provider)}
                        >
                          {unlinkingId === link.provider ? '…' : 'Unlink'}
                        </button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}

            {entry.lastSyncedAt ? (
              <p className="mt-3 text-xs text-gray-400">
                Last synced {new Date(entry.lastSyncedAt).toLocaleString()}
              </p>
            ) : null}
          </GlassCard>
        </div>
      </div>

      {showLinkDialog ? (
        <SearchLinkDialog
          libraryEntryId={libraryEntryId}
          mediaKind={title.mediaKind}
          existingLinks={entry.providerLinks}
          onClose={() => setShowLinkDialog(false)}
          onLinked={() => {
            setShowLinkDialog(false);
            void loadEntry();
          }}
        />
      ) : null}
    </>
  );
}
