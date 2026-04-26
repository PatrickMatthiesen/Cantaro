import { useState, useEffect, useCallback, useRef } from 'react';
import { GlassCard, GradientButton } from '../components/ui/GlassComponents';
import { mediaApi } from '../services/mediaApi';
import { mainMediaProviderId, mediaProviderCatalog } from '../services/mediaProviders';
import {
  clearStoredValue,
  formatTimestamp,
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../services/mediaRefreshCache';
import type { MediaLibraryListItemDto, MediaLibraryQueryParams, MediaProviderAccountStatusDto } from '../services/mediaApi';

const NORMALIZED_STATUS_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'planning', label: 'Planning' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
  { value: 'repeating', label: 'Rewatching / Rereading' },
];

const MEDIA_KIND_OPTIONS = [
  { value: '', label: 'All types' },
  { value: 'anime', label: 'Anime' },
  { value: 'manga', label: 'Manga' },
  { value: 'lightNovel', label: 'Light Novel' },
  { value: 'oneShot', label: 'One Shot' },
];

const SORT_OPTIONS = [
  { value: 'updatedAt', label: 'Last updated' },
  { value: 'title', label: 'Title' },
  { value: 'status', label: 'Status' },
  { value: 'progress', label: 'Progress' },
];

const FIRST_PROVIDER_ID = mainMediaProviderId;

function storedListNameKey(providerId: string): string {
  return `cantaro.media.provider.${providerId}.lastListName`;
}

function statusLabel(status: string): string {
  return NORMALIZED_STATUS_OPTIONS.find((o) => o.value === status)?.label ?? status;
}

function mediaKindLabel(kind: string): string {
  return MEDIA_KIND_OPTIONS.find((o) => o.value === kind)?.label ?? kind;
}

function statusColor(status: string): string {
  switch (status) {
    case 'current': return 'bg-emerald-100 text-emerald-800';
    case 'completed': return 'bg-blue-100 text-blue-800';
    case 'planning': return 'bg-violet-100 text-violet-800';
    case 'paused': return 'bg-amber-100 text-amber-800';
    case 'dropped': return 'bg-rose-100 text-rose-800';
    case 'repeating': return 'bg-cyan-100 text-cyan-800';
    default: return 'bg-gray-100 text-gray-700';
  }
}

function progressText(entry: MediaLibraryListItemDto): string {
  switch (entry.primaryProgressDimension) {
    case 'episode': {
      const current = entry.progressEpisodes ?? 0;
      const total = entry.episodeCount;
      return total ? `Ep ${current} / ${total}` : `Ep ${current}`;
    }
    case 'chapter': {
      const current = entry.progressChapters ?? 0;
      const total = entry.chapterCount;
      return total ? `Ch ${current} / ${total}` : `Ch ${current}`;
    }
    case 'volume': {
      const current = entry.progressVolumes ?? 0;
      const total = entry.volumeCount;
      return total ? `Vol ${current} / ${total}` : `Vol ${current}`;
    }
    default:
      return '';
  }
}

interface LibraryEntryCardProps {
  entry: MediaLibraryListItemDto;
  onClick: () => void;
}

function LibraryArtwork({ posterUrl, title }: { posterUrl?: string; title: string }) {
  const [failed, setFailed] = useState(false);

  if (!posterUrl || failed) {
    return (
      <div className="flex h-full w-full items-center justify-center rounded-2xl bg-linear-to-br from-indigo-100 to-purple-100">
        <span className="text-2xl" aria-hidden>🎌</span>
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

function LibraryEntryCard({ entry, onClick }: LibraryEntryCardProps) {
  const progress = progressText(entry);

  return (
    <button type="button" onClick={onClick} className="w-full text-left">
      <GlassCard interactive className="group p-4 transition">
        <div className="flex items-start gap-4">
          <div className="h-24 w-16 shrink-0 overflow-hidden rounded-2xl bg-white/70 shadow-sm">
            <LibraryArtwork posterUrl={entry.posterUrl} title={entry.canonicalTitle} />
          </div>

          <div className="flex min-w-0 flex-1 items-start justify-between gap-3">
            <div className="min-w-0 flex-1">
              <p className="truncate font-semibold text-gray-900 transition-colors group-hover:text-indigo-700">
                {entry.canonicalTitle}
              </p>
              {entry.originalTitle && entry.originalTitle !== entry.canonicalTitle ? (
                <p className="mt-0.5 truncate text-xs text-gray-500">{entry.originalTitle}</p>
              ) : null}
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <span className="rounded-full bg-indigo-50 px-2.5 py-0.5 text-xs font-medium text-indigo-700">
                  {mediaKindLabel(entry.mediaKind)}
                </span>
                {entry.rawListName ? (
                  <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-700">
                    {entry.rawListName}
                  </span>
                ) : null}
                <span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${statusColor(entry.normalizedStatus)}`}>
                  {statusLabel(entry.normalizedStatus)}
                </span>
                {progress ? (
                  <span className="text-xs text-gray-500">{progress}</span>
                ) : null}
              </div>
            </div>
            <div className="shrink-0 text-right">
              <p className="text-xs text-gray-400">{entry.provider}</p>
              {!entry.isConnected ? (
                <span className="mt-1 block text-xs text-amber-600">Not synced</span>
              ) : null}
            </div>
          </div>
        </div>
      </GlassCard>
    </button>
  );
}

interface MediaLibraryPageProps {
  onNavigateHome: () => void;
  onNavigateProviders: () => void;
  onNavigateEntry: (id: string) => void;
}

export function MediaLibraryPage({ onNavigateHome, onNavigateProviders, onNavigateEntry }: MediaLibraryPageProps) {
  const [filters, setFilters] = useState<MediaLibraryQueryParams>(() => ({
    provider: FIRST_PROVIDER_ID,
    listName: readStoredValue(storedListNameKey(FIRST_PROVIDER_ID)) || undefined,
    sortBy: 'updatedAt',
    sortDir: 'desc',
    page: 1,
    pageSize: 24,
  }));
  const [items, setItems] = useState<MediaLibraryListItemDto[]>([]);
  const [availableListNames, setAvailableListNames] = useState<string[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [providerStatus, setProviderStatus] = useState<MediaProviderAccountStatusDto | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [lastRemoteCheckAt, setLastRemoteCheckAt] = useState<string | null>(() => readStoredValue(remoteCheckTimestampKey(FIRST_PROVIDER_ID)));
  const filtersRef = useRef(filters);

  useEffect(() => {
    filtersRef.current = filters;
  }, [filters]);

  const loadLibrary = useCallback(async (params: MediaLibraryQueryParams) => {
    setIsLoading(true);
    setError(null);
    try {
      const result = await mediaApi.getLibrary(params);
      setItems(result.items);
      setAvailableListNames(result.availableListNames);
      setTotalPages(result.totalPages);
      setTotalCount(result.totalCount);
    } catch (err) {
      setAvailableListNames([]);
      setError(err instanceof Error ? err.message : 'Failed to load library');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadLibrary(filters);
  }, [filters, loadLibrary]);

  useEffect(() => {
    if (filters.provider !== FIRST_PROVIDER_ID) {
      return;
    }

    const storageKey = storedListNameKey(FIRST_PROVIDER_ID);
    if (filters.listName) {
      writeStoredValue(storageKey, filters.listName);
      return;
    }

    clearStoredValue(storageKey);
  }, [filters.listName, filters.provider]);

  useEffect(() => {
    if (!filters.listName || availableListNames.length === 0 || availableListNames.includes(filters.listName)) {
      return;
    }

    setFilters((prev) => ({ ...prev, listName: undefined, page: 1 }));
  }, [availableListNames, filters.listName]);

  const refreshFromRemote = useCallback(async () => {
    setIsRefreshing(true);
    setRefreshError(null);

    try {
      const result = await mediaApi.importLibrary(FIRST_PROVIDER_ID);
      writeStoredValue(remoteCheckTimestampKey(FIRST_PROVIDER_ID), result.importedAt);
      setLastRemoteCheckAt(result.importedAt);
      await loadLibrary(filtersRef.current);
    } catch (err) {
      setRefreshError(err instanceof Error ? err.message : 'Failed to refresh from AniList');
    } finally {
      setIsRefreshing(false);
    }
  }, [loadLibrary]);

  useEffect(() => {
    let isCancelled = false;

    const loadProviderState = async () => {
      try {
        const status = await mediaApi.getProviderStatus(FIRST_PROVIDER_ID);
        if (isCancelled) {
          return;
        }

        setProviderStatus(status);
        if (status.isConnected && isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(FIRST_PROVIDER_ID)))) {
          await refreshFromRemote();
        }
      } catch (err) {
        if (!isCancelled) {
          setRefreshError(err instanceof Error ? err.message : 'Failed to check AniList status');
        }
      }
    };

    void loadProviderState();

    return () => {
      isCancelled = true;
    };
  }, [refreshFromRemote]);

  const updateFilter = <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => {
    setFilters((prev) => ({ ...prev, [key]: value, page: 1 }));
  };

  const updateProviderFilter = (provider: string | undefined) => {
    setFilters((prev) => ({
      ...prev,
      provider,
      listName: provider === FIRST_PROVIDER_ID
        ? readStoredValue(storedListNameKey(FIRST_PROVIDER_ID)) || undefined
        : undefined,
      page: 1,
    }));
  };

  const formattedLastRemoteCheckAt = formatTimestamp(lastRemoteCheckAt);
  const hasActiveFilters = Boolean(
    filters.status
    || filters.mediaKind
    || filters.listName
    || (filters.provider && filters.provider !== FIRST_PROVIDER_ID),
  );

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto max-w-5xl space-y-6 px-6 pt-8 pb-16">
        <header className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Media</p>
            <h1 className="mt-1 text-3xl font-bold">My Library</h1>
            {!isLoading && totalCount > 0 ? (
              <p className="mt-1 text-sm text-gray-500">{totalCount.toLocaleString()} entries</p>
            ) : null}
            {providerStatus?.isConnected && formattedLastRemoteCheckAt ? (
              <p className="mt-1 text-xs text-gray-500">Last checked AniList: {formattedLastRemoteCheckAt}</p>
            ) : null}
          </div>
          <div className="flex gap-2">
            <GradientButton tone="soft" onClick={onNavigateProviders}>
              Providers
            </GradientButton>
            <GradientButton tone="soft" onClick={onNavigateHome}>
              ← Home
            </GradientButton>
          </div>
        </header>

        {refreshError ? (
          <GlassCard className="p-4">
            <p className="text-sm text-rose-700">{refreshError}</p>
          </GlassCard>
        ) : null}

        {/* Filters */}
        <GlassCard className="p-4">
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">Status</label>
              <select
                className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400"
                value={filters.status ?? ''}
                onChange={(e) => updateFilter('status', e.target.value || undefined)}
              >
                {NORMALIZED_STATUS_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>{o.label}</option>
                ))}
              </select>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">Type</label>
              <select
                className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400"
                value={filters.mediaKind ?? ''}
                onChange={(e) => updateFilter('mediaKind', e.target.value || undefined)}
              >
                {MEDIA_KIND_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>{o.label}</option>
                ))}
              </select>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">Provider</label>
              <select
                className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400"
                value={filters.provider ?? ''}
                onChange={(e) => updateProviderFilter(e.target.value || undefined)}
              >
                <option value="">All providers</option>
                {mediaProviderCatalog.map((p) => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">List</label>
              <select
                className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400 disabled:cursor-not-allowed disabled:opacity-60"
                value={filters.listName ?? ''}
                onChange={(e) => updateFilter('listName', e.target.value || undefined)}
                disabled={availableListNames.length === 0 || filters.provider !== FIRST_PROVIDER_ID}
              >
                <option value="">All lists</option>
                {availableListNames.map((listName) => (
                  <option key={listName} value={listName}>{listName}</option>
                ))}
              </select>
            </div>

            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-gray-500 uppercase tracking-wide">Sort by</label>
              <div className="flex gap-1">
                <select
                  className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-2 focus:ring-indigo-400"
                  value={filters.sortBy ?? 'updatedAt'}
                  onChange={(e) => updateFilter('sortBy', e.target.value)}
                >
                  {SORT_OPTIONS.map((o) => (
                    <option key={o.value} value={o.value}>{o.label}</option>
                  ))}
                </select>
                <button
                  type="button"
                  className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-600 hover:bg-white transition"
                  onClick={() => setFilters((prev) => ({ ...prev, sortDir: prev.sortDir === 'asc' ? 'desc' : 'asc', page: 1 }))}
                  title={filters.sortDir === 'asc' ? 'Ascending — click to switch' : 'Descending — click to switch'}
                >
                  {filters.sortDir === 'asc' ? '↑' : '↓'}
                </button>
              </div>
            </div>

            {providerStatus?.isConnected ? (
              <div className="ml-auto flex flex-col gap-1">
                <span className="text-xs font-medium text-gray-500 uppercase tracking-wide">Reload</span>
                <button
                  type="button"
                  className="inline-flex h-10 items-center justify-center rounded-xl border border-gray-200 bg-white/80 px-4 text-sm font-medium text-gray-700 transition hover:bg-white disabled:cursor-not-allowed disabled:opacity-60"
                  onClick={() => void refreshFromRemote()}
                  disabled={isRefreshing}
                  aria-busy={isRefreshing}
                  title="Reload the primary provider library"
                >
                  {isRefreshing ? 'Reloading…' : '↻ Reload'}
                </button>
              </div>
            ) : null}
          </div>
        </GlassCard>

        {/* Content */}
        {error ? (
          <GlassCard className="p-6">
            <p className="text-sm text-rose-700">{error}</p>
            <div className="mt-3">
              <GradientButton tone="soft" onClick={() => void loadLibrary(filters)}>Retry</GradientButton>
            </div>
          </GlassCard>
        ) : isLoading ? (
          <div className="grid gap-3 sm:grid-cols-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <GlassCard key={i} className="h-24 animate-pulse bg-white/50" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <GlassCard className="p-8 text-center">
            <p className="text-gray-500">No library entries found.</p>
            <p className="mt-1 text-sm text-gray-400">
              {hasActiveFilters
                ? 'Try removing some filters.'
                : 'Import your library from a provider to get started.'}
            </p>
            {!hasActiveFilters ? (
              <div className="mt-4">
                <GradientButton gradient="from-blue-500 to-cyan-500" onClick={onNavigateProviders}>
                  Go to Providers
                </GradientButton>
              </div>
            ) : null}
          </GlassCard>
        ) : (
          <>
            <div className="grid gap-3 sm:grid-cols-2">
              {items.map((entry) => (
                <LibraryEntryCard
                  key={entry.id}
                  entry={entry}
                  onClick={() => onNavigateEntry(entry.id)}
                />
              ))}
            </div>

            {totalPages > 1 ? (
              <div className="flex items-center justify-center gap-2">
                <GradientButton
                  tone="soft"
                  disabled={filters.page === 1}
                  onClick={() => setFilters((prev) => ({ ...prev, page: Math.max(1, (prev.page ?? 1) - 1) }))}
                >
                  ← Previous
                </GradientButton>
                <span className="text-sm text-gray-600">
                  Page {filters.page ?? 1} of {totalPages}
                </span>
                <GradientButton
                  tone="soft"
                  disabled={(filters.page ?? 1) >= totalPages}
                  onClick={() => setFilters((prev) => ({ ...prev, page: (prev.page ?? 1) + 1 }))}
                >
                  Next →
                </GradientButton>
              </div>
            ) : null}
          </>
        )}
      </div>
    </div>
  );
}
