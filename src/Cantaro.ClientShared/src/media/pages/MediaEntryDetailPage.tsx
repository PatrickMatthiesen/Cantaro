import { useState, useEffect, useCallback, useRef, type Dispatch, type ReactNode, type SetStateAction } from 'react';
import { EntryOverviewCard } from '../components/media-entry-detail/EntryOverviewCard';
import { ProviderLinksCard } from '../components/media-entry-detail/ProviderLinksCard';
import { providerAvailabilityKey, type ProviderAvailabilityMap } from '../components/media-entry-detail/providerAvailability';
import { GlassCard, GradientButton, Snackbar, type ShowSnackbar, type SnackbarNotification } from '../../ui';
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
  { value: 'planned', label: 'Planning' },
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

interface StatusDraft {
  selectedStatus: string;
  progressEpisodes: number | undefined;
  progressChapters: number | undefined;
  progressVolumes: number | undefined;
}

function useTimedSnackbar(timeoutMs = 3000) {
  const [snackbar, setSnackbar] = useState<SnackbarNotification | null>(null);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }
  }, []);

  const showSnackbar = useCallback((notification: SnackbarNotification) => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }

    setSnackbar(notification);
    timeoutRef.current = setTimeout(() => {
      setSnackbar(null);
      timeoutRef.current = null;
    }, timeoutMs);
  }, [timeoutMs]);

  return { snackbar, showSnackbar };
}

function useEntryDetailState(libraryEntryId: string) {
  const [entry, setEntry] = useState<MediaLibraryEntryDetailDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [progressEpisodes, setProgressEpisodes] = useState<number | undefined>();
  const [progressChapters, setProgressChapters] = useState<number | undefined>();
  const [progressVolumes, setProgressVolumes] = useState<number | undefined>();
  const [selectedStatus, setSelectedStatus] = useState('');

  const applyEntryData = useCallback((data: MediaLibraryEntryDetailDto) => {
    setEntry(data);
    setProgressEpisodes(data.progressEpisodes);
    setProgressChapters(data.progressChapters);
    setProgressVolumes(data.progressVolumes);
    setSelectedStatus(data.normalizedStatus);
  }, []);

  const loadEntry = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      const data = await mediaApi.getLibraryEntry(libraryEntryId);
      applyEntryData(data);
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load entry'));
    } finally {
      setIsLoading(false);
    }
  }, [applyEntryData, libraryEntryId]);

  const reloadEntry = useCallback(async () => {
    const data = await mediaApi.getLibraryEntry(libraryEntryId);
    applyEntryData(data);
  }, [applyEntryData, libraryEntryId]);

  useEffect(() => {
    void loadEntry();
  }, [loadEntry]);

  return {
    entry,
    setEntry,
    isLoading,
    error,
    loadEntry,
    reloadEntry,
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
  reloadEntry: () => Promise<void>,
  showSnackbar: ShowSnackbar,
) {
  const [isRefreshingRemote, setIsRefreshingRemote] = useState(false);
  const refreshInFlightRef = useRef(false);
  const entryId = entry?.id;
  const entryProvider = entry?.provider;
  const entryIsConnected = entry?.isConnected;

  useEffect(() => {
    if (!entryId || !entryIsConnected || refreshInFlightRef.current) {
      return;
    }

    const refreshProviderId = entryProvider || mainMediaProviderId;
    if (!isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(refreshProviderId)))) {
      return;
    }

    let isCancelled = false;

    const refreshFromRemote = async () => {
      refreshInFlightRef.current = true;
      setIsRefreshingRemote(true);

      try {
        const result = await mediaApi.importLibrary(refreshProviderId);
        if (isCancelled) {
          return;
        }

        writeStoredValue(remoteCheckTimestampKey(refreshProviderId), result.importedAt);
        await reloadEntry();
      } catch (refreshError) {
        if (!isCancelled) {
          showSnackbar({
            message: getPrefixedErrorMessage(refreshError, 'Failed to refresh entry'),
            variant: 'error',
          });
        }
      } finally {
        refreshInFlightRef.current = false;
        if (!isCancelled) {
          setIsRefreshingRemote(false);
        }
      }
    };

    void refreshFromRemote();

    return () => {
      isCancelled = true;
    };
  }, [entryId, entryIsConnected, entryProvider, reloadEntry, showSnackbar]);

  return isRefreshingRemote;
}

function useManualRemoteRefresh(
  entry: MediaLibraryEntryDetailDto | null,
  reloadEntry: () => Promise<void>,
  showSnackbar: ShowSnackbar,
) {
  const [isRefreshingRemote, setIsRefreshingRemote] = useState(false);

  const handleRefreshFromProvider = useCallback(async () => {
    if (!entry?.isConnected) {
      return;
    }

    const refreshProviderId = entry.provider || mainMediaProviderId;
    setIsRefreshingRemote(true);

    try {
      const result = await mediaApi.importLibrary(refreshProviderId);
      writeStoredValue(remoteCheckTimestampKey(refreshProviderId), result.importedAt);
      await reloadEntry();
      showSnackbar({ message: 'Status refreshed from provider', variant: 'success' });
    } catch (refreshError) {
      showSnackbar({
        message: getPrefixedErrorMessage(refreshError, 'Failed to refresh from provider'),
        variant: 'error',
      });
    } finally {
      setIsRefreshingRemote(false);
    }
  }, [entry, reloadEntry, showSnackbar]);

  return { isRefreshingRemote, handleRefreshFromProvider };
}

function getStatusChanges(entry: MediaLibraryEntryDetailDto, draft: StatusDraft) {
  return {
    statusChanged: draft.selectedStatus !== entry.normalizedStatus,
    progressChanged:
      draft.progressEpisodes !== entry.progressEpisodes
      || draft.progressChapters !== entry.progressChapters
      || draft.progressVolumes !== entry.progressVolumes,
  };
}

async function saveStatusChanges(
  libraryEntryId: string,
  draft: StatusDraft,
  changes: ReturnType<typeof getStatusChanges>,
) {
  if (changes.statusChanged) {
    await mediaApi.updateStatus(libraryEntryId, { status: draft.selectedStatus });
  }

  if (changes.progressChanged) {
    await mediaApi.updateProgress(libraryEntryId, {
      progressEpisodes: draft.progressEpisodes,
      progressChapters: draft.progressChapters,
      progressVolumes: draft.progressVolumes,
    });
  }
}

function applySavedStatus(
  current: MediaLibraryEntryDetailDto | null,
  draft: StatusDraft,
) {
  return current
    ? {
      ...current,
      normalizedStatus: draft.selectedStatus,
      progressEpisodes: draft.progressEpisodes,
      progressChapters: draft.progressChapters,
      progressVolumes: draft.progressVolumes,
    }
    : current;
}

function useStatusSaveAction(
  libraryEntryId: string,
  entry: MediaLibraryEntryDetailDto | null,
  selectedStatus: string,
  progressEpisodes: number | undefined,
  progressChapters: number | undefined,
  progressVolumes: number | undefined,
  setEntry: Dispatch<SetStateAction<MediaLibraryEntryDetailDto | null>>,
  showSnackbar: ShowSnackbar,
) {
  const [isSavingStatus, setIsSavingStatus] = useState(false);

  const handleSaveStatus = useCallback(async () => {
    if (!entry) return;

    const draft = { selectedStatus, progressEpisodes, progressChapters, progressVolumes };
    const changes = getStatusChanges(entry, draft);

    setIsSavingStatus(true);
    try {
      await saveStatusChanges(libraryEntryId, draft, changes);
      setEntry((current) => applySavedStatus(current, draft));
      showSnackbar({ message: 'Status saved', variant: 'success' });
    } catch (saveError) {
      showSnackbar({
        message: getPrefixedErrorMessage(saveError, 'Failed to save status'),
        variant: 'error',
      });
    } finally {
      setIsSavingStatus(false);
    }
  }, [
    entry,
    libraryEntryId,
    progressChapters,
    progressEpisodes,
    progressVolumes,
    selectedStatus,
    setEntry,
    showSnackbar,
  ]);

  return { isSavingStatus, handleSaveStatus };
}

function useProviderUnlinkAction(
  libraryEntryId: string,
  setEntry: Dispatch<SetStateAction<MediaLibraryEntryDetailDto | null>>,
  showSnackbar: ShowSnackbar,
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
      showSnackbar({
        message: getPrefixedErrorMessage(unlinkError, 'Failed to unlink'),
        variant: 'error',
      });
    } finally {
      setUnlinkingId(null);
    }
  }, [libraryEntryId, setEntry, showSnackbar]);

  return { unlinkingId, handleUnlink };
}


// ── Progress controls ─────────────────────────────────────────────────────────

interface ProgressFieldProps {
  label: string;
  value: number | undefined;
  max?: number;
  onChange: (value: number) => void;
}

function clampProgressValue(value: number, max?: number) {
  const lowerBoundedValue = Math.max(0, Math.round(value));
  return max ? Math.min(lowerBoundedValue, max) : lowerBoundedValue;
}

function ProgressField({ label, value, max, onChange }: ProgressFieldProps) {
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

interface DetailHeaderProps {
  mediaKind: string;
  isConnected: boolean;
  onNavigateBack: () => void;
}

interface StatusCardProps {
  title: MediaLibraryEntryDetailDto['title'];
  selectedStatus: string;
  progressEpisodes: number | undefined;
  progressChapters: number | undefined;
  progressVolumes: number | undefined;
  supportsEpisodes: boolean;
  supportsChapters: boolean;
  supportsVolumes: boolean;
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  canRefreshProgress: boolean;
  updatedAt: string;
  onStatusChange: (value: string) => void;
  onProgressEpisodesChange: (value: number) => void;
  onProgressChaptersChange: (value: number) => void;
  onProgressVolumesChange: (value: number) => void;
  onSaveStatus: () => void;
  onRefreshProgress: () => void;
}

interface StatusActionRowProps {
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  canRefreshProgress: boolean;
  onSaveStatus: () => void;
  onRefreshProgress: () => void;
}

interface EntryDetailPanelsProps extends Pick<
  MediaEntryDetailContentProps,
  'entry'
  | 'availabilityByProviderLink'
  | 'unlinkingId'
  | 'progressEpisodes'
  | 'progressChapters'
  | 'progressVolumes'
  | 'selectedStatus'
  | 'isRefreshingProgress'
  | 'isSavingStatus'
  | 'onSetShowLinkDialog'
  | 'onSetProgressEpisodes'
  | 'onSetProgressChapters'
  | 'onSetProgressVolumes'
  | 'onSetSelectedStatus'
  | 'onRefreshProgress'
  | 'onSaveStatus'
  | 'onUnlink'
> {}

interface MediaEntryDetailContentProps {
  libraryEntryId: string;
  entry: MediaLibraryEntryDetailDto;
  embedded: boolean;
  availabilityByProviderLink: ProviderAvailabilityMap;
  isRefreshingProgress: boolean;
  isSavingStatus: boolean;
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
  onRefreshProgress: () => void;
  onSaveStatus: () => void;
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

function getProgressCapabilities(title: MediaLibraryEntryDetailDto['title']) {
  const dim = title.primaryProgressDimension;
  return {
    supportsEpisodes: dim === 'episode',
    supportsChapters: dim === 'chapter',
    supportsVolumes: dim === 'volume' || dim === 'chapter',
  };
}

function getEntryStatusChanged(props: EntryDetailPanelsProps) {
  return props.selectedStatus !== props.entry.normalizedStatus
    || props.progressEpisodes !== props.entry.progressEpisodes
    || props.progressChapters !== props.entry.progressChapters
    || props.progressVolumes !== props.entry.progressVolumes;
}

function EntryDetailPanels(props: EntryDetailPanelsProps) {
  const { entry } = props;
  const capabilities = getProgressCapabilities(entry.title);

  return (
    <>
      <EntryOverviewCard entry={entry} nextRelease={formatNextReleaseDisplay(entry.nextReleaseAt)} />
      <StatusCard
        title={entry.title}
        selectedStatus={props.selectedStatus}
        hasStatusChanged={getEntryStatusChanged(props)}
        isSavingStatus={props.isSavingStatus}
        progressEpisodes={props.progressEpisodes}
        progressChapters={props.progressChapters}
        progressVolumes={props.progressVolumes}
        {...capabilities}
        isRefreshingProgress={props.isRefreshingProgress}
        canRefreshProgress={entry.isConnected}
        updatedAt={entry.updatedAt}
        onStatusChange={props.onSetSelectedStatus}
        onProgressEpisodesChange={props.onSetProgressEpisodes}
        onProgressChaptersChange={props.onSetProgressChapters}
        onProgressVolumesChange={props.onSetProgressVolumes}
        onSaveStatus={props.onSaveStatus}
        onRefreshProgress={props.onRefreshProgress}
      />
      <ProviderLinksCard
        providerLinks={entry.providerLinks}
        availabilityByProviderLink={props.availabilityByProviderLink}
        unlinkingId={props.unlinkingId}
        lastSyncedAt={entry.lastSyncedAt}
        onLinkProvider={() => props.onSetShowLinkDialog(true)}
        onUnlink={props.onUnlink}
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

function StatusCardHeader({ selectedStatus, updatedAt, onStatusChange }: Pick<StatusCardProps, 'selectedStatus' | 'updatedAt' | 'onStatusChange'>) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 className="text-sm font-semibold tracking-wide text-gray-500 uppercase">Status</h2>
        <p className="mt-1 text-xs text-gray-400">
          Last updated {new Date(updatedAt).toLocaleDateString()}
        </p>
      </div>
      <select
        className="min-w-52 rounded-xl border border-gray-200 bg-white/80 px-4 py-2 text-sm text-gray-700 focus:ring-2 focus:ring-indigo-400 focus:outline-none"
        value={selectedStatus}
        onChange={(event) => onStatusChange(event.target.value)}
      >
        {NORMALIZED_STATUSES.map((status) => (
          <option key={status.value} value={status.value}>{status.label}</option>
        ))}
      </select>
    </div>
  );
}

type StatusProgressFieldsProps = Pick<
  StatusCardProps,
  'title'
  | 'progressEpisodes'
  | 'progressChapters'
  | 'progressVolumes'
  | 'supportsEpisodes'
  | 'supportsChapters'
  | 'supportsVolumes'
  | 'onProgressEpisodesChange'
  | 'onProgressChaptersChange'
  | 'onProgressVolumesChange'
>;

function StatusProgressFields(props: StatusProgressFieldsProps) {
  return (
    <div className="mt-4 grid gap-3 sm:grid-cols-3">
      {props.supportsEpisodes ? (
        <ProgressField label="Episodes" value={props.progressEpisodes} max={props.title.episodeCount} onChange={props.onProgressEpisodesChange} />
      ) : null}
      {props.supportsChapters ? (
        <ProgressField label="Chapters" value={props.progressChapters} max={props.title.chapterCount} onChange={props.onProgressChaptersChange} />
      ) : null}
      {props.supportsVolumes ? (
        <ProgressField label="Volumes" value={props.progressVolumes} max={props.title.volumeCount} onChange={props.onProgressVolumesChange} />
      ) : null}
    </div>
  );
}

function StatusCard(props: StatusCardProps) {
  return (
    <GlassCard className="p-6">
      <StatusCardHeader {...props} />
      <StatusProgressFields {...props} />
      <StatusActionRow
        hasStatusChanged={props.hasStatusChanged}
        isSavingStatus={props.isSavingStatus}
        isRefreshingProgress={props.isRefreshingProgress}
        canRefreshProgress={props.canRefreshProgress}
        onSaveStatus={props.onSaveStatus}
        onRefreshProgress={props.onRefreshProgress}
      />
    </GlassCard>
  );
}

function StatusActionRow({
  hasStatusChanged,
  isSavingStatus,
  isRefreshingProgress,
  canRefreshProgress,
  onSaveStatus,
  onRefreshProgress,
}: StatusActionRowProps) {
  return (
    <div className="mt-4 flex flex-wrap items-center gap-3">
      <GradientButton
        gradient="from-indigo-500 to-purple-500"
        onClick={onSaveStatus}
        disabled={isStatusSaveDisabled(hasStatusChanged, isSavingStatus, isRefreshingProgress)}
        aria-busy={isSavingStatus}
      >
        {getStatusSaveLabel(isSavingStatus)}
      </GradientButton>
      <GradientButton
        tone="soft"
        onClick={onRefreshProgress}
        disabled={isStatusRefreshDisabled(canRefreshProgress, isSavingStatus, isRefreshingProgress)}
        aria-busy={isRefreshingProgress}
        title={canRefreshProgress ? 'Refresh status from provider' : 'Entry must be synced to refresh status'}
      >
        {getProgressRefreshLabel(isRefreshingProgress)}
      </GradientButton>
    </div>
  );
}

function isStatusSaveDisabled(
  hasStatusChanged: boolean,
  isSavingStatus: boolean,
  isRefreshingProgress: boolean,
) {
  return [!hasStatusChanged, isSavingStatus, isRefreshingProgress].some(Boolean);
}

function isStatusRefreshDisabled(
  canRefreshProgress: boolean,
  isSavingStatus: boolean,
  isRefreshingProgress: boolean,
) {
  return [!canRefreshProgress, isSavingStatus, isRefreshingProgress].some(Boolean);
}

function getStatusSaveLabel(isSavingStatus: boolean) {
  return isSavingStatus ? 'Saving…' : 'Save status';
}

function getProgressRefreshLabel(isRefreshingProgress: boolean) {
  return isRefreshingProgress ? 'Refreshing…' : 'Refresh';
}

// ── Entry detail page ──────────────────────────────────────────────────────────

interface MediaEntryDetailPageProps {
  libraryEntryId: string;
  onNavigateBack: () => void;
  embedded?: boolean;
}

interface MediaEntryDetailPageViewProps extends Omit<MediaEntryDetailContentProps, 'entry'> {
  entry: MediaLibraryEntryDetailDto | null;
  isLoading: boolean;
  error: string | null;
  snackbar: SnackbarNotification | null;
}

function MediaEntryDetailBody(props: MediaEntryDetailContentProps & { mediaKind: string }) {
  return (
    <DetailPageLayout embedded={props.embedded}>
      <DetailHeader mediaKind={props.mediaKind} isConnected={props.entry.isConnected} onNavigateBack={props.onNavigateBack} />
      <EntryDetailPanels {...props} />
    </DetailPageLayout>
  );
}

function MediaEntryDetailContent(props: MediaEntryDetailContentProps) {
  const mediaKind = props.entry.title.mediaKind;

  return (
    <>
      <MediaEntryDetailBody {...props} mediaKind={mediaKind} />
      <EntryLinkDialog
        showLinkDialog={props.showLinkDialog}
        libraryEntryId={props.libraryEntryId}
        mediaKind={mediaKind}
        existingLinks={props.entry.providerLinks}
        onClose={() => props.onSetShowLinkDialog(false)}
        onLinked={() => {
          props.onSetShowLinkDialog(false);
          void props.onLoadEntry();
        }}
      />
    </>
  );
}

function MediaEntryDetailPageView({
  entry,
  isLoading,
  error,
  snackbar,
  ...contentProps
}: MediaEntryDetailPageViewProps) {
  const { embedded, onNavigateBack, onLoadEntry } = contentProps;

  if (isLoading) {
    return <DetailLoadingState embedded={embedded} />;
  }

  if (error || !entry) {
    return <DetailErrorState error={error} embedded={embedded} onNavigateBack={onNavigateBack} onRetry={onLoadEntry} />;
  }

  return (
    <>
      <MediaEntryDetailContent
        {...contentProps}
        entry={entry}
      />
      <Snackbar notification={snackbar} />
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
    reloadEntry,
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
  const { snackbar, showSnackbar } = useTimedSnackbar();
  useRemoteEntryRefresh(entry, reloadEntry, showSnackbar);
  const {
    isRefreshingRemote: isRefreshingProgress,
    handleRefreshFromProvider,
  } = useManualRemoteRefresh(entry, reloadEntry, showSnackbar);
  const { isSavingStatus, handleSaveStatus } = useStatusSaveAction(
    libraryEntryId,
    entry,
    selectedStatus,
    progressEpisodes,
    progressChapters,
    progressVolumes,
    setEntry,
    showSnackbar,
  );
  const { unlinkingId, handleUnlink } = useProviderUnlinkAction(libraryEntryId, setEntry, showSnackbar);
  const [showLinkDialog, setShowLinkDialog] = useState(false);

  return (
    <MediaEntryDetailPageView
      libraryEntryId={libraryEntryId}
      onNavigateBack={onNavigateBack}
      embedded={embedded}
      entry={entry}
      isLoading={isLoading}
      error={error}
      onLoadEntry={loadEntry}
      availabilityByProviderLink={availabilityByProviderLink}
      snackbar={snackbar}
      isRefreshingProgress={isRefreshingProgress}
      isSavingStatus={isSavingStatus}
      showLinkDialog={showLinkDialog}
      unlinkingId={unlinkingId}
      progressEpisodes={progressEpisodes}
      progressChapters={progressChapters}
      progressVolumes={progressVolumes}
      selectedStatus={selectedStatus}
      onSetShowLinkDialog={setShowLinkDialog}
      onSetProgressEpisodes={setProgressEpisodes}
      onSetProgressChapters={setProgressChapters}
      onSetProgressVolumes={setProgressVolumes}
      onSetSelectedStatus={setSelectedStatus}
      onRefreshProgress={() => void handleRefreshFromProvider()}
      onSaveStatus={() => void handleSaveStatus()}
      onUnlink={(providerId) => void handleUnlink(providerId)}
    />
  );
}
