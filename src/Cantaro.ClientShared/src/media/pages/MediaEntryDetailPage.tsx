import { useState, useEffect, useCallback, useRef, type Dispatch, type ReactNode, type SetStateAction } from 'react';
import { EntryOverviewCard } from '../components/media-entry-detail/EntryOverviewCard';
import { ProviderLinksCard } from '../components/media-entry-detail/ProviderLinksCard';
import { providerAvailabilityKey, type ProviderAvailabilityMap } from '../components/media-entry-detail/providerAvailability';
import { GlassCard, GradientButton } from '../../ui';
import { SearchLinkDialog } from '../components/SearchLinkDialog';
import { mediaApi } from '../services/mediaApi';
import { formatNextReleaseDisplay, mediaKindLabel } from '../services/mediaFormatting';
import { mainMediaProviderId } from '../services/mediaProviders';
import {
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../services/mediaRefreshCache';
import type {
  MediaLibraryEntryDetailDto,
  MediaProviderLinkSummaryDto,
} from '../services/mediaApi';

const NORMALIZED_STATUSES = [
  { value: 'current', label: 'Watching / Reading' },
  { value: 'completed', label: 'Completed' },
  { value: 'planning', label: 'Planning' },
  { value: 'paused', label: 'Paused' },
  { value: 'dropped', label: 'Dropped' },
  { value: 'repeating', label: 'Rewatching / Rereading' },
];

function getErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

function getPrefixedErrorMessage(error: unknown, fallbackMessage: string): string {
  return `Error: ${getErrorMessage(error, fallbackMessage)}`;
}

function buildAvailabilityMap(
  providerLinks: MediaProviderLinkSummaryDto[],
  current: ProviderAvailabilityMap,
): ProviderAvailabilityMap {
  const next: ProviderAvailabilityMap = {};
  for (const link of providerLinks) {
    const key = providerAvailabilityKey(link.provider, link.externalId);
    next[key] = current[key] ?? { status: 'loading', links: [] };
  }

  return next;
}

async function loadAvailabilityStates(providerLinks: MediaProviderLinkSummaryDto[]) {
  return Promise.all(providerLinks.map(async (link) => {
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
    } catch (error) {
      return {
        key,
        state: {
          status: 'error' as const,
          links: [],
          error: getErrorMessage(error, 'Failed to load availability'),
        },
      };
    }
  }));
}

function useTimedMessage(timeoutMs = 3000) {
  const [message, setMessage] = useState<string | null>(null);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }
  }, []);

  const showMessage = useCallback((nextMessage: string) => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }

    setMessage(nextMessage);
    timeoutRef.current = setTimeout(() => {
      setMessage(null);
      timeoutRef.current = null;
    }, timeoutMs);
  }, [timeoutMs]);

  return { message, setMessage, showMessage };
}

function useEntryDetailState(libraryEntryId: string) {
  const [entry, setEntry] = useState<MediaLibraryEntryDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
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
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load entry'));
    } finally {
      setIsLoading(false);
    }
  }, [libraryEntryId]);

  useEffect(() => {
    void loadEntry();
  }, [loadEntry]);

  return {
    entry,
    setEntry,
    isLoading,
    error,
    loadEntry,
    progressEpisodes,
    setProgressEpisodes,
    progressChapters,
    setProgressChapters,
    progressVolumes,
    setProgressVolumes,
    selectedStatus,
    setSelectedStatus,
  };
}

function useProviderAvailability(entry: MediaLibraryEntryDetailDto | null) {
  const [availabilityByProviderLink, setAvailabilityByProviderLink] = useState<ProviderAvailabilityMap>({});

  useEffect(() => {
    if (!entry || entry.providerLinks.length === 0) {
      setAvailabilityByProviderLink({});
      return;
    }

    let isCancelled = false;
    setAvailabilityByProviderLink((current) => buildAvailabilityMap(entry.providerLinks, current));

    const loadAvailability = async () => {
      const results = await loadAvailabilityStates(entry.providerLinks);
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

  return availabilityByProviderLink;
}

function useRemoteEntryRefresh(
  entry: MediaLibraryEntryDetailDto | null,
  loadEntry: () => Promise<void>,
  setSaveMessage: (message: string) => void,
) {
  const [isRefreshingRemote, setIsRefreshingRemote] = useState(false);

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
      } catch (refreshError) {
        if (!isCancelled) {
          setSaveMessage(getPrefixedErrorMessage(refreshError, 'Failed to refresh entry'));
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
  }, [entry, isRefreshingRemote, loadEntry, setSaveMessage]);

  return isRefreshingRemote;
}

function useProgressSaveAction(
  libraryEntryId: string,
  entry: MediaLibraryEntryDetailDto | null,
  progressEpisodes: number | undefined,
  progressChapters: number | undefined,
  progressVolumes: number | undefined,
  setEntry: Dispatch<SetStateAction<MediaLibraryEntryDetailDto | null>>,
  showSaveMessage: (message: string) => void,
  setSaveMessage: (message: string) => void,
) {
  const [isSavingProgress, setIsSavingProgress] = useState(false);

  const handleSaveProgress = useCallback(async () => {
    if (!entry) {
      return;
    }

    setIsSavingProgress(true);
    try {
      await mediaApi.updateProgress(libraryEntryId, {
        progressEpisodes,
        progressChapters,
        progressVolumes,
      });
      setEntry((current) => current ? { ...current, progressEpisodes, progressChapters, progressVolumes } : current);
      showSaveMessage('Progress saved');
    } catch (saveError) {
      setSaveMessage(getPrefixedErrorMessage(saveError, 'Failed to save'));
    } finally {
      setIsSavingProgress(false);
    }
  }, [entry, libraryEntryId, progressEpisodes, progressChapters, progressVolumes, setEntry, setSaveMessage, showSaveMessage]);

  return { isSavingProgress, handleSaveProgress };
}

function useStatusSaveAction(
  libraryEntryId: string,
  entry: MediaLibraryEntryDetailDto | null,
  selectedStatus: string,
  setEntry: Dispatch<SetStateAction<MediaLibraryEntryDetailDto | null>>,
  showSaveMessage: (message: string) => void,
  setSaveMessage: (message: string) => void,
) {
  const [isSavingStatus, setIsSavingStatus] = useState(false);

  const handleSaveStatus = useCallback(async () => {
    if (!entry) {
      return;
    }

    setIsSavingStatus(true);
    try {
      await mediaApi.updateStatus(libraryEntryId, { status: selectedStatus });
      setEntry((current) => current ? { ...current, normalizedStatus: selectedStatus } : current);
      showSaveMessage('Status saved');
    } catch (saveError) {
      setSaveMessage(getPrefixedErrorMessage(saveError, 'Failed to save'));
    } finally {
      setIsSavingStatus(false);
    }
  }, [entry, libraryEntryId, selectedStatus, setEntry, setSaveMessage, showSaveMessage]);

  return { isSavingStatus, handleSaveStatus };
}

function useAutoProgressAction(
  libraryEntryId: string,
  entry: MediaLibraryEntryDetailDto | null,
  setEntry: Dispatch<SetStateAction<MediaLibraryEntryDetailDto | null>>,
  showSaveMessage: (message: string) => void,
  setSaveMessage: (message: string) => void,
) {
  const handleToggleAutoProgress = useCallback(async () => {
    if (!entry) {
      return;
    }

    const newValue = !entry.autoProgressFromObservations;
    try {
      await mediaApi.updateAutoProgress(libraryEntryId, { enabled: newValue });
      setEntry((current) => current ? { ...current, autoProgressFromObservations: newValue } : current);
      showSaveMessage(newValue ? 'Auto-progress enabled' : 'Auto-progress disabled');
    } catch (updateError) {
      setSaveMessage(getPrefixedErrorMessage(updateError, 'Failed to update'));
    }
  }, [entry, libraryEntryId, setEntry, setSaveMessage, showSaveMessage]);

  return handleToggleAutoProgress;
}

function useProviderUnlinkAction(
  libraryEntryId: string,
  setEntry: Dispatch<SetStateAction<MediaLibraryEntryDetailDto | null>>,
  setSaveMessage: (message: string) => void,
) {
  const [unlinkingId, setUnlinkingId] = useState<string | null>(null);

  const handleUnlink = useCallback(async (providerId: string) => {
    setUnlinkingId(providerId);

    try {
      await mediaApi.unlinkProvider(libraryEntryId, providerId);
      setEntry((current) => current
        ? { ...current, providerLinks: current.providerLinks.filter((link) => link.provider !== providerId) }
        : current);
    } catch (unlinkError) {
      setSaveMessage(getPrefixedErrorMessage(unlinkError, 'Failed to unlink'));
    } finally {
      setUnlinkingId(null);
    }
  }, [libraryEntryId, setEntry, setSaveMessage]);

  return { unlinkingId, handleUnlink };
}


// ── Progress controls ─────────────────────────────────────────────────────────

interface ProgressFieldProps {
  label: string;
  value: number | undefined;
  max?: number;
  supported: boolean;
  onChange: (value: number) => void;
}

function clampProgressValue(value: number, max?: number) {
  const lowerBoundedValue = Math.max(0, Math.round(value));
  return max ? Math.min(lowerBoundedValue, max) : lowerBoundedValue;
}

function ProgressField({ label, value, max, supported, onChange }: ProgressFieldProps) {
  if (!supported) {
    return (
      <div className="rounded-xl bg-gray-50 px-4 py-3">
        <p className="text-xs font-medium tracking-wide text-gray-400 uppercase">{label}</p>
        <p className="mt-1 text-sm text-gray-400 italic">Not tracked for this type</p>
      </div>
    );
  }

  const currentValue = clampProgressValue(value ?? 0, max);
  const sliderMax = Math.max(max ?? 100, currentValue, 1);
  const canDecrease = currentValue > 0;
  const canIncrease = max ? currentValue < max : true;
  const setProgressValue = (nextValue: number) => {
    onChange(clampProgressValue(nextValue, max));
  };

  return (
    <div className="rounded-xl bg-white/70 px-4 py-3">
      <p className="text-xs font-medium tracking-wide text-gray-500 uppercase">{label}</p>
      <div className="mt-2 flex items-center gap-3">
        <button
          type="button"
          className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-gray-200 bg-white text-lg font-semibold leading-none text-gray-600 shadow-sm transition hover:border-indigo-200 hover:text-indigo-600 disabled:cursor-not-allowed disabled:opacity-40"
          onClick={() => setProgressValue(currentValue - 1)}
          disabled={!canDecrease}
          aria-label={`Decrease ${label.toLowerCase()}`}
        >
          -
        </button>
        <div className="min-w-0 flex-1">
          <input
            type="range"
            min={0}
            max={sliderMax}
            step={1}
            value={currentValue}
            onChange={(event) => setProgressValue(Number(event.target.value))}
            className="h-2 w-full cursor-pointer accent-indigo-500"
            aria-label={`${label} progress`}
          />
        </div>
        <button
          type="button"
          className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-gray-200 bg-white text-lg font-semibold leading-none text-gray-600 shadow-sm transition hover:border-indigo-200 hover:text-indigo-600 disabled:cursor-not-allowed disabled:opacity-40"
          onClick={() => setProgressValue(currentValue + 1)}
          disabled={!canIncrease}
          aria-label={`Increase ${label.toLowerCase()}`}
        >
          +
        </button>
      </div>
      <div className="mt-2 flex items-baseline gap-1">
        <span className="text-xl font-semibold text-gray-800">{currentValue}</span>
        {max ? <span className="text-sm text-gray-400">/ {max}</span> : null}
      </div>
    </div>
  );
}

// ── Manual search / link dialog ────────────────────────────────────────────────

interface DetailPageLayoutProps {
  children: ReactNode;
  className?: string;
  embedded?: boolean;
}

interface DetailBannerProps {
  message: string;
}

interface DetailHeaderProps {
  mediaKind: string;
  isConnected: boolean;
  onNavigateBack: () => void;
}

interface StatusCardProps {
  selectedStatus: string;
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  updatedAt: string;
  onStatusChange: (value: string) => void;
  onSaveStatus: () => void;
}

interface ProgressCardProps {
  title: MediaLibraryEntryDetailDto['title'];
  progressEpisodes: number | undefined;
  progressChapters: number | undefined;
  progressVolumes: number | undefined;
  supportsEpisodes: boolean;
  supportsChapters: boolean;
  supportsVolumes: boolean;
  hasProgressChanged: boolean;
  isSavingProgress: boolean;
  onProgressEpisodesChange: (value: number) => void;
  onProgressChaptersChange: (value: number) => void;
  onProgressVolumesChange: (value: number) => void;
  onSaveProgress: () => void;
}

interface AutoProgressCardProps {
  enabled: boolean;
  onToggle: () => void;
}

interface MediaEntryDetailContentProps {
  libraryEntryId: string;
  entry: MediaLibraryEntryDetailDto;
  embedded: boolean;
  availabilityByProviderLink: ProviderAvailabilityMap;
  isRefreshingRemote: boolean;
  isSavingProgress: boolean;
  isSavingStatus: boolean;
  saveMessage: string | null;
  showLinkDialog: boolean;
  unlinkingId: string | null;
  progressEpisodes: number | undefined;
  progressChapters: number | undefined;
  progressVolumes: number | undefined;
  selectedStatus: string;
  onNavigateBack: () => void;
  onLoadEntry: () => Promise<void>;
  onSetShowLinkDialog: (visible: boolean) => void;
  onSetProgressEpisodes: (value: number) => void;
  onSetProgressChapters: (value: number) => void;
  onSetProgressVolumes: (value: number) => void;
  onSetSelectedStatus: (value: string) => void;
  onSaveProgress: () => void;
  onSaveStatus: () => void;
  onToggleAutoProgress: () => void;
  onUnlink: (providerId: string) => void;
}

function DetailPageLayout({ children, className = 'space-y-6', embedded = false }: DetailPageLayoutProps) {
  if (embedded) {
    return (
      <div className={`mx-auto max-w-4xl ${className}`}>
        {children}
      </div>
    );
  }

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className={`relative z-10 mx-auto max-w-4xl px-6 pt-8 pb-16 ${className}`}>
        {children}
      </div>
    </div>
  );
}

function DetailLoadingState({ embedded = false }: { embedded?: boolean }) {
  return (
    <DetailPageLayout className="" embedded={embedded}>
      <GlassCard className="h-96 animate-pulse" />
    </DetailPageLayout>
  );
}

function DetailErrorState({ error, embedded = false, onNavigateBack, onRetry }: { error: string | null; embedded?: boolean; onNavigateBack: () => void; onRetry: () => Promise<void> }) {
  return (
    <DetailPageLayout className="space-y-4" embedded={embedded}>
      <GradientButton tone="soft" onClick={onNavigateBack}>← Back to library</GradientButton>
      <GlassCard className="p-6">
        <p className="text-rose-700">{error ?? 'Entry not found'}</p>
        <div className="mt-3">
          <GradientButton tone="soft" onClick={() => void onRetry()}>Retry</GradientButton>
        </div>
      </GlassCard>
    </DetailPageLayout>
  );
}

function DetailSaveBanner({ message }: DetailBannerProps) {
  return (
    <div className={`rounded-2xl px-4 py-3 text-sm font-medium ${message.startsWith('Error:') ? 'bg-rose-50 text-rose-700' : 'bg-emerald-50 text-emerald-800'}`}>
      {message}
    </div>
  );
}

function DetailRefreshBanner() {
  return (
    <div className="rounded-2xl bg-sky-50 px-4 py-3 text-sm font-medium text-sky-700">
      Refreshing provider data…
    </div>
  );
}

function DetailHeader({ mediaKind, isConnected, onNavigateBack }: DetailHeaderProps) {
  return (
    <header className="flex flex-wrap items-center gap-3">
      <GradientButton tone="soft" onClick={onNavigateBack}>
        ← Library
      </GradientButton>
      <div className="flex flex-wrap items-center gap-2">
        <span className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-700">
          {mediaKindLabel(mediaKind)}
        </span>
        <span className={`rounded-full px-3 py-1 text-xs font-semibold ${isConnected ? 'bg-green-100 text-green-800' : 'bg-amber-100 text-amber-800'}`}>
          {isConnected ? 'Synced' : 'Not synced'}
        </span>
      </div>
    </header>
  );
}

function EntryDetailAlerts({ saveMessage, isRefreshingRemote }: { saveMessage: string | null; isRefreshingRemote: boolean }) {
  return (
    <>
      {saveMessage ? <DetailSaveBanner message={saveMessage} /> : null}
      {isRefreshingRemote ? <DetailRefreshBanner /> : null}
    </>
  );
}

function EntryDetailPanels({
  entry,
  availabilityByProviderLink,
  unlinkingId,
  progressEpisodes,
  progressChapters,
  progressVolumes,
  selectedStatus,
  isSavingProgress,
  isSavingStatus,
  onSetShowLinkDialog,
  onSetProgressEpisodes,
  onSetProgressChapters,
  onSetProgressVolumes,
  onSetSelectedStatus,
  onSaveProgress,
  onSaveStatus,
  onToggleAutoProgress,
  onUnlink,
}: Pick<
  MediaEntryDetailContentProps,
  'entry'
  | 'availabilityByProviderLink'
  | 'unlinkingId'
  | 'progressEpisodes'
  | 'progressChapters'
  | 'progressVolumes'
  | 'selectedStatus'
  | 'isSavingProgress'
  | 'isSavingStatus'
  | 'onSetShowLinkDialog'
  | 'onSetProgressEpisodes'
  | 'onSetProgressChapters'
  | 'onSetProgressVolumes'
  | 'onSetSelectedStatus'
  | 'onSaveProgress'
  | 'onSaveStatus'
  | 'onToggleAutoProgress'
  | 'onUnlink'
>) {
  const { title } = entry;
  const dim = title.primaryProgressDimension;
  const supportsEpisodes = dim === 'episode';
  const supportsChapters = dim === 'chapter';
  const supportsVolumes = dim === 'volume' || dim === 'chapter';
  const hasProgressChanged =
    progressEpisodes !== entry.progressEpisodes
    || progressChapters !== entry.progressChapters
    || progressVolumes !== entry.progressVolumes;
  const hasStatusChanged = selectedStatus !== entry.normalizedStatus;
  const nextRelease = formatNextReleaseDisplay(entry.nextReleaseAt);

  return (
    <>
      <EntryOverviewCard entry={entry} nextRelease={nextRelease} />
      <StatusCard
        selectedStatus={selectedStatus}
        hasStatusChanged={hasStatusChanged}
        isSavingStatus={isSavingStatus}
        updatedAt={entry.updatedAt}
        onStatusChange={onSetSelectedStatus}
        onSaveStatus={onSaveStatus}
      />
      <ProgressCard
        title={title}
        progressEpisodes={progressEpisodes}
        progressChapters={progressChapters}
        progressVolumes={progressVolumes}
        supportsEpisodes={supportsEpisodes}
        supportsChapters={supportsChapters}
        supportsVolumes={supportsVolumes}
        hasProgressChanged={hasProgressChanged}
        isSavingProgress={isSavingProgress}
        onProgressEpisodesChange={onSetProgressEpisodes}
        onProgressChaptersChange={onSetProgressChapters}
        onProgressVolumesChange={onSetProgressVolumes}
        onSaveProgress={onSaveProgress}
      />
      <AutoProgressCard enabled={entry.autoProgressFromObservations} onToggle={onToggleAutoProgress} />
      <ProviderLinksCard
        providerLinks={entry.providerLinks}
        availabilityByProviderLink={availabilityByProviderLink}
        unlinkingId={unlinkingId}
        lastSyncedAt={entry.lastSyncedAt}
        onLinkProvider={() => onSetShowLinkDialog(true)}
        onUnlink={onUnlink}
      />
    </>
  );
}

function EntryLinkDialog({
  showLinkDialog,
  libraryEntryId,
  mediaKind,
  existingLinks,
  onClose,
  onLinked,
}: {
  showLinkDialog: boolean;
  libraryEntryId: string;
  mediaKind: string;
  existingLinks: MediaProviderLinkSummaryDto[];
  onClose: () => void;
  onLinked: () => void;
}) {
  if (!showLinkDialog) {
    return null;
  }

  return (
    <SearchLinkDialog
      libraryEntryId={libraryEntryId}
      mediaKind={mediaKind}
      existingLinks={existingLinks}
      onClose={onClose}
      onLinked={onLinked}
    />
  );
}

function StatusCard({ selectedStatus, hasStatusChanged, isSavingStatus, updatedAt, onStatusChange, onSaveStatus }: StatusCardProps) {
  return (
    <GlassCard className="p-6">
      <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase">Status</h2>
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <select
          className="rounded-xl border border-gray-200 bg-white/80 px-4 py-2 text-sm text-gray-700 focus:ring-2 focus:ring-indigo-400 focus:outline-none"
          value={selectedStatus}
          onChange={(event) => onStatusChange(event.target.value)}
        >
          {NORMALIZED_STATUSES.map((status) => (
            <option key={status.value} value={status.value}>{status.label}</option>
          ))}
        </select>
        {hasStatusChanged ? (
          <GradientButton
            gradient="from-indigo-500 to-purple-500"
            onClick={onSaveStatus}
            disabled={isSavingStatus}
            aria-busy={isSavingStatus}
          >
            {isSavingStatus ? 'Saving…' : 'Save status'}
          </GradientButton>
        ) : null}
        <span className="text-xs text-gray-400">
          Last updated {new Date(updatedAt).toLocaleDateString()}
        </span>
      </div>
    </GlassCard>
  );
}

function ProgressCard({
  title,
  progressEpisodes,
  progressChapters,
  progressVolumes,
  supportsEpisodes,
  supportsChapters,
  supportsVolumes,
  hasProgressChanged,
  isSavingProgress,
  onProgressEpisodesChange,
  onProgressChaptersChange,
  onProgressVolumesChange,
  onSaveProgress,
}: ProgressCardProps) {
  return (
    <GlassCard className="p-6">
      <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase">Progress</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-3">
        <ProgressField
          label="Episodes"
          value={progressEpisodes}
          max={title.episodeCount}
          supported={supportsEpisodes}
          onChange={onProgressEpisodesChange}
        />
        <ProgressField
          label="Chapters"
          value={progressChapters}
          max={title.chapterCount}
          supported={supportsChapters}
          onChange={onProgressChaptersChange}
        />
        <ProgressField
          label="Volumes"
          value={progressVolumes}
          max={title.volumeCount}
          supported={supportsVolumes}
          onChange={onProgressVolumesChange}
        />
      </div>
      <div className="mt-4">
        <GradientButton
          gradient="from-indigo-500 to-purple-500"
          onClick={onSaveProgress}
          disabled={!hasProgressChanged || isSavingProgress}
          aria-busy={isSavingProgress}
        >
          {isSavingProgress ? 'Saving…' : 'Save progress'}
        </GradientButton>
      </div>
      <p className="mt-3 text-xs text-gray-400">
        Editing progress here updates Cantaro only. Changes are not automatically pushed to your connected provider.
      </p>
    </GlassCard>
  );
}

function AutoProgressCard({ enabled, onToggle }: AutoProgressCardProps) {
  return (
    <GlassCard className="p-6">
      <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase">Auto-progress</h2>
      <div className="mt-3 flex items-start gap-4">
        <button
          type="button"
          role="switch"
          aria-checked={enabled}
          onClick={onToggle}
          className={`relative mt-0.5 inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors focus:ring-2 focus:ring-indigo-400 focus:ring-offset-2 focus:outline-none ${enabled ? 'bg-indigo-500' : 'bg-gray-200'}`}
        >
          <span
            aria-hidden
            className={`inline-block h-5 w-5 transform rounded-full bg-white shadow transition-transform ${enabled ? 'translate-x-5' : 'translate-x-0'}`}
          />
        </button>
        <div>
          <p className="text-sm font-medium text-gray-800">
            {enabled ? 'Enabled' : 'Disabled'}
          </p>
          <p className="mt-1 text-xs text-gray-500">
            When enabled, Cantaro may advance your progress counter when the browser extension
            detects you watching an episode. Progress only moves forward and is subject to
            backend matching confidence — it will not overwrite remote changes.
          </p>
        </div>
      </div>
    </GlassCard>
  );
}


// ── Entry detail page ──────────────────────────────────────────────────────────

interface MediaEntryDetailPageProps {
  libraryEntryId: string;
  onNavigateBack: () => void;
  embedded?: boolean;
}

function MediaEntryDetailContent({
  libraryEntryId,
  entry,
  embedded,
  availabilityByProviderLink,
  isRefreshingRemote,
  isSavingProgress,
  isSavingStatus,
  saveMessage,
  showLinkDialog,
  unlinkingId,
  progressEpisodes,
  progressChapters,
  progressVolumes,
  selectedStatus,
  onNavigateBack,
  onLoadEntry,
  onSetShowLinkDialog,
  onSetProgressEpisodes,
  onSetProgressChapters,
  onSetProgressVolumes,
  onSetSelectedStatus,
  onSaveProgress,
  onSaveStatus,
  onToggleAutoProgress,
  onUnlink,
}: MediaEntryDetailContentProps) {
  const mediaKind = entry.title.mediaKind;

  return (
    <>
      <DetailPageLayout embedded={embedded}>
        <DetailHeader mediaKind={mediaKind} isConnected={entry.isConnected} onNavigateBack={onNavigateBack} />
        <EntryDetailAlerts saveMessage={saveMessage} isRefreshingRemote={isRefreshingRemote} />
        <EntryDetailPanels
          entry={entry}
          availabilityByProviderLink={availabilityByProviderLink}
          unlinkingId={unlinkingId}
          progressEpisodes={progressEpisodes}
          progressChapters={progressChapters}
          progressVolumes={progressVolumes}
          selectedStatus={selectedStatus}
          isSavingProgress={isSavingProgress}
          isSavingStatus={isSavingStatus}
          onSetShowLinkDialog={onSetShowLinkDialog}
          onSetProgressEpisodes={onSetProgressEpisodes}
          onSetProgressChapters={onSetProgressChapters}
          onSetProgressVolumes={onSetProgressVolumes}
          onSetSelectedStatus={onSetSelectedStatus}
          onSaveProgress={onSaveProgress}
          onSaveStatus={onSaveStatus}
          onToggleAutoProgress={onToggleAutoProgress}
          onUnlink={onUnlink}
        />
      </DetailPageLayout>

      <EntryLinkDialog
        showLinkDialog={showLinkDialog}
        libraryEntryId={libraryEntryId}
        mediaKind={mediaKind}
        existingLinks={entry.providerLinks}
        onClose={() => onSetShowLinkDialog(false)}
        onLinked={() => {
          onSetShowLinkDialog(false);
          void onLoadEntry();
        }}
      />
    </>
  );
}

export function MediaEntryDetailPage({ libraryEntryId, onNavigateBack, embedded = false }: MediaEntryDetailPageProps) {
  const {
    entry,
    setEntry,
    isLoading,
    error,
    loadEntry,
    progressEpisodes,
    setProgressEpisodes,
    progressChapters,
    setProgressChapters,
    progressVolumes,
    setProgressVolumes,
    selectedStatus,
    setSelectedStatus,
  } = useEntryDetailState(libraryEntryId);
  const availabilityByProviderLink = useProviderAvailability(entry);
  const { message: saveMessage, setMessage: setSaveMessage, showMessage: showSaveMessage } = useTimedMessage();
  const isRefreshingRemote = useRemoteEntryRefresh(entry, loadEntry, setSaveMessage);
  const { isSavingProgress, handleSaveProgress } = useProgressSaveAction(
    libraryEntryId,
    entry,
    progressEpisodes,
    progressChapters,
    progressVolumes,
    setEntry,
    showSaveMessage,
    setSaveMessage,
  );
  const { isSavingStatus, handleSaveStatus } = useStatusSaveAction(
    libraryEntryId,
    entry,
    selectedStatus,
    setEntry,
    showSaveMessage,
    setSaveMessage,
  );
  const handleToggleAutoProgress = useAutoProgressAction(
    libraryEntryId,
    entry,
    setEntry,
    showSaveMessage,
    setSaveMessage,
  );
  const { unlinkingId, handleUnlink } = useProviderUnlinkAction(libraryEntryId, setEntry, setSaveMessage);
  const [showLinkDialog, setShowLinkDialog] = useState(false);

  if (isLoading) {
    return <DetailLoadingState embedded={embedded} />;
  }

  if (error || !entry) {
    return <DetailErrorState error={error} embedded={embedded} onNavigateBack={onNavigateBack} onRetry={loadEntry} />;
  }

  return (
    <MediaEntryDetailContent
      libraryEntryId={libraryEntryId}
      entry={entry}
      embedded={embedded}
      availabilityByProviderLink={availabilityByProviderLink}
      isRefreshingRemote={isRefreshingRemote}
      isSavingProgress={isSavingProgress}
      isSavingStatus={isSavingStatus}
      saveMessage={saveMessage}
      showLinkDialog={showLinkDialog}
      unlinkingId={unlinkingId}
      progressEpisodes={progressEpisodes}
      progressChapters={progressChapters}
      progressVolumes={progressVolumes}
      selectedStatus={selectedStatus}
      onNavigateBack={onNavigateBack}
      onLoadEntry={loadEntry}
      onSetShowLinkDialog={setShowLinkDialog}
      onSetProgressEpisodes={setProgressEpisodes}
      onSetProgressChapters={setProgressChapters}
      onSetProgressVolumes={setProgressVolumes}
      onSetSelectedStatus={setSelectedStatus}
      onSaveProgress={() => void handleSaveProgress()}
      onSaveStatus={() => void handleSaveStatus()}
      onToggleAutoProgress={() => void handleToggleAutoProgress()}
      onUnlink={(providerId) => void handleUnlink(providerId)}
    />
  );
}
