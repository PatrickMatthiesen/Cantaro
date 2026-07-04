import { useCallback, useEffect, useRef, useState, type CSSProperties, type Dispatch, type ReactNode, type SetStateAction } from 'react';
import {
  CalendarDays,
  CheckCircle2,
  CircleCheck,
  Clock3,
  Flame,
  Heart,
  Info,
  Minus,
  MoreVertical,
  Play,
  Plus,
  RefreshCcw,
  Save,
  Star,
  Tv,
} from 'lucide-react';
import { DetailArtwork, SanitizedSynopsis } from '../components/media-entry-detail/EntryDisplayPrimitives';
import { providerAvailabilityKey, type ProviderAvailabilityMap, type ProviderAvailabilityState } from '../components/media-entry-detail/providerAvailability';
import { MediaProviderIcon } from '../components/MediaProviderIcon';
import { SearchLinkDialog } from '../components/SearchLinkDialog';
import { GradientButton, Snackbar, type ShowSnackbar, type SnackbarNotification } from '../../ui';
import { mediaApi } from '../services/mediaApi';
import { formatNextReleaseDisplay, mediaKindLabel } from '../services/mediaFormatting';
import { mainMediaProviderId, mediaProviderCatalog } from '../services/mediaProviders';
import {
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../services/mediaRefreshCache';
import type {
  MediaLibraryEntryDetailDto,
  MediaLibraryImportEventDto,
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

const DETAIL_TABS = ['Overview', 'Progress', 'Providers', 'Franchise', 'Characters', 'Details'];

interface StatusDraft {
  selectedStatus: string;
  progressEpisodes: number | undefined;
  progressChapters: number | undefined;
  progressVolumes: number | undefined;
}

interface ProgressSummary {
  label: string;
  noun: string;
  value: number | undefined;
  total?: number;
  nextLabel: string;
}

interface MediaEntryDetailPageProps {
  libraryEntryId: string;
  onNavigateBack: () => void;
  embedded?: boolean;
  onHeadingChange?: (heading: { eyebrow: string; title: string; details?: string[]; hidden?: boolean }) => void;
}

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

interface MediaEntryDetailPageViewProps extends Omit<MediaEntryDetailContentProps, 'entry'> {
  entry: MediaLibraryEntryDetailDto | null;
  isLoading: boolean;
  error: string | null;
  snackbar: SnackbarNotification | null;
}

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
    let eventSource: EventSource | null = null;

    const refreshFromRemote = async () => {
      refreshInFlightRef.current = true;
      setIsRefreshingRemote(true);

      try {
        const importRequest = await mediaApi.importLibrary(refreshProviderId);
        if (isCancelled) return;

        eventSource = await mediaApi.subscribeToImportEvents(
          refreshProviderId,
          importRequest.importId,
          (event: MediaLibraryImportEventDto) => {
            if (isCancelled) {
              return;
            }

            if (event.status === 'completed') {
              writeStoredValue(remoteCheckTimestampKey(refreshProviderId), event.occurredAt);
              void reloadEntry();
              refreshInFlightRef.current = false;
              setIsRefreshingRemote(false);
              eventSource?.close();
              return;
            }

            if (event.status === 'failed') {
              showSnackbar({
                message: event.errorMessage || 'Failed to refresh entry',
                variant: 'error',
              });
              refreshInFlightRef.current = false;
              setIsRefreshingRemote(false);
              eventSource?.close();
            }
          },
          () => {
            if (!isCancelled) {
              refreshInFlightRef.current = false;
              setIsRefreshingRemote(false);
            }
          },
        );
      } catch (refreshError) {
        if (!isCancelled) {
          showSnackbar({
            message: getPrefixedErrorMessage(refreshError, 'Failed to refresh entry'),
            variant: 'error',
          });
          refreshInFlightRef.current = false;
          setIsRefreshingRemote(false);
        }
      }
    };

    void refreshFromRemote();

    return () => {
      isCancelled = true;
      eventSource?.close();
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
    let eventSource: EventSource | null = null;
    setIsRefreshingRemote(true);

    try {
      const importRequest = await mediaApi.importLibrary(refreshProviderId);
      eventSource = await mediaApi.subscribeToImportEvents(
        refreshProviderId,
        importRequest.importId,
        (event: MediaLibraryImportEventDto) => {
          if (event.status === 'completed') {
            writeStoredValue(remoteCheckTimestampKey(refreshProviderId), event.occurredAt);
            void reloadEntry();
            showSnackbar({ message: 'Status refreshed from provider', variant: 'success' });
            setIsRefreshingRemote(false);
            eventSource?.close();
            return;
          }

          if (event.status === 'failed') {
            showSnackbar({
              message: event.errorMessage || 'Failed to refresh from provider',
              variant: 'error',
            });
            setIsRefreshingRemote(false);
            eventSource?.close();
          }
        },
        () => setIsRefreshingRemote(false),
      );
    } catch (refreshError) {
      showSnackbar({
        message: getPrefixedErrorMessage(refreshError, 'Failed to refresh from provider'),
        variant: 'error',
      });
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

function progressKindLabel(title: MediaLibraryEntryDetailDto['title']) {
  if (title.primaryProgressDimension === 'episode') {
    return title.episodeCount ? 'TV Series' : 'Episode tracking';
  }

  if (title.primaryProgressDimension === 'chapter') {
    return 'Manga';
  }

  if (title.primaryProgressDimension === 'volume') {
    return 'Volumes';
  }

  return releaseStatusLabel(title.releaseStatusDimension);
}

function clampProgressValue(value: number, max?: number) {
  const lowerBoundedValue = Math.max(0, Math.round(value));
  return max ? Math.min(lowerBoundedValue, max) : lowerBoundedValue;
}

function getPrimaryProgressSummary(
  title: MediaLibraryEntryDetailDto['title'],
  progressEpisodes: number | undefined,
  progressChapters: number | undefined,
  progressVolumes: number | undefined,
): ProgressSummary {
  if (title.primaryProgressDimension === 'chapter') {
    return {
      label: 'Chapters read',
      noun: 'chapters',
      value: progressChapters,
      total: title.chapterCount,
      nextLabel: `Next chapter ${clampProgressValue((progressChapters ?? 0) + 1, title.chapterCount)}`,
    };
  }

  if (title.primaryProgressDimension === 'volume') {
    return {
      label: 'Volumes read',
      noun: 'volumes',
      value: progressVolumes,
      total: title.volumeCount,
      nextLabel: `Next volume ${clampProgressValue((progressVolumes ?? 0) + 1, title.volumeCount)}`,
    };
  }

  return {
    label: 'Watched',
    noun: 'episodes',
    value: progressEpisodes,
    total: title.episodeCount,
    nextLabel: `Next up: Episode ${clampProgressValue((progressEpisodes ?? 0) + 1, title.episodeCount)}`,
  };
}

function getProgressPercent(summary: ProgressSummary) {
  if (!summary.total || summary.total <= 0) {
    return 0;
  }

  return Math.round((Math.min(summary.value ?? 0, summary.total) / summary.total) * 100);
}

function getRemainingLabel(summary: ProgressSummary) {
  if (!summary.total) {
    return 'Total unknown';
  }

  const remaining = Math.max(summary.total - (summary.value ?? 0), 0);
  return remaining === 1 ? `1 ${summary.noun.slice(0, -1)} left` : `${remaining} ${summary.noun} left`;
}

function getProgressCapabilities(title: MediaLibraryEntryDetailDto['title']) {
  const dim = title.primaryProgressDimension;
  return {
    supportsEpisodes: dim === 'episode',
    supportsChapters: dim === 'chapter',
    supportsVolumes: dim === 'volume' || dim === 'chapter',
  };
}

function getEntryStatusChanged(props: Pick<
  MediaEntryDetailContentProps,
  'entry' | 'selectedStatus' | 'progressEpisodes' | 'progressChapters' | 'progressVolumes'
>) {
  return props.selectedStatus !== props.entry.normalizedStatus
    || props.progressEpisodes !== props.entry.progressEpisodes
    || props.progressChapters !== props.entry.progressChapters
    || props.progressVolumes !== props.entry.progressVolumes;
}

function getStatusSaveLabel(isSavingStatus: boolean) {
  return isSavingStatus ? 'Saving...' : 'Save progress';
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

function DetailPageLayout({ children, className = '', embedded = false }: { children: ReactNode; className?: string; embedded?: boolean }) {
  if (embedded) {
    return (
      <div className={`media-detail-shell ${className}`}>
        {children}
      </div>
    );
  }

  return (
    <div className="media-detail-standalone">
      <div className={`media-detail-shell ${className}`}>
        {children}
      </div>
    </div>
  );
}

function DetailLoadingState({ embedded = false }: { embedded?: boolean }) {
  return (
    <DetailPageLayout embedded={embedded}>
      <div className="media-detail-skeleton" />
    </DetailPageLayout>
  );
}

function DetailErrorState({
  error,
  embedded = false,
  onNavigateBack,
  onRetry,
}: {
  error: string | null;
  embedded?: boolean;
  onNavigateBack: () => void;
  onRetry: () => Promise<void>;
}) {
  return (
    <DetailPageLayout className="space-y-4" embedded={embedded}>
      <GradientButton tone="soft" onClick={onNavigateBack}>Back to library</GradientButton>
      <div className="media-detail-empty-panel">
        <p>{error ?? 'Entry not found'}</p>
        <button type="button" onClick={() => void onRetry()}>Retry</button>
      </div>
    </DetailPageLayout>
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

function DetailTopBar({
  mediaKind,
  isConnected,
  onNavigateBack,
}: {
  mediaKind: string;
  isConnected: boolean;
  onNavigateBack: () => void;
}) {
  return (
    <header className="media-detail-topbar">
      <button type="button" className="media-detail-icon-button" onClick={onNavigateBack} aria-label="Back to library">
        <span aria-hidden>←</span>
      </button>
      <div className="media-detail-topbar-pills">
        <span className="media-detail-pill media-detail-pill--violet">{mediaKindLabel(mediaKind)}</span>
        <span className={`media-detail-pill ${isConnected ? 'media-detail-pill--success' : 'media-detail-pill--warning'}`}>
          {isConnected ? 'Synced' : 'Not synced'}
        </span>
      </div>
    </header>
  );
}

function MediaHero({
  entry,
  progressSummary,
  onNavigateBack,
}: {
  entry: MediaLibraryEntryDetailDto;
  progressSummary: ProgressSummary;
  onNavigateBack: () => void;
}) {
  const { title } = entry;
  const nextRelease = formatNextReleaseDisplay(entry.nextReleaseAt);
  const titleCounts = [
    title.episodeCount ? `${title.episodeCount} Episodes` : null,
    title.chapterCount ? `${title.chapterCount} Chapters` : null,
    title.volumeCount ? `${title.volumeCount} Volumes` : null,
  ].filter((value): value is string => Boolean(value));

  return (
    <section className="media-detail-hero">
      {title.posterUrl ? (
        <img className="media-detail-hero-bg" src={title.posterUrl} alt="" aria-hidden />
      ) : null}
      <div className="media-detail-hero-scrim" aria-hidden />
      <div className="media-detail-hero-content">
        <DetailTopBar mediaKind={title.mediaKind} isConnected={entry.isConnected} onNavigateBack={onNavigateBack} />
        <div className="media-detail-hero-grid">
          <div className="media-detail-poster">
            <DetailArtwork posterUrl={title.posterUrl} title={title.canonicalTitle} />
          </div>
          <div className="media-detail-title-stack">
            <div className="media-detail-tag-row">
              <span className="media-detail-dot media-detail-dot--violet" />
              <span>{mediaKindLabel(title.mediaKind)}</span>
              <span className="media-detail-dot media-detail-dot--blue" />
              <span>{progressKindLabel(title)}</span>
            </div>
            <h2>{title.canonicalTitle}</h2>
            {title.originalTitle && title.originalTitle !== title.canonicalTitle ? (
              <p className="media-detail-original-title">{title.originalTitle}</p>
            ) : null}
            <div className="media-detail-meta-row">
              {title.startYear ? (
                <span><CalendarDays aria-hidden />{title.startYear}</span>
              ) : null}
              {titleCounts.map((count) => (
                <span key={count}><Tv aria-hidden />{count}</span>
              ))}
              {nextRelease ? <span><Clock3 aria-hidden />{nextRelease.relative}</span> : null}
            </div>
            <div className="media-detail-rating-pill">
              <Star aria-hidden />
              <span>Library progress {getProgressPercent(progressSummary)}%</span>
            </div>
            {title.synopsis ? (
              <SanitizedSynopsis html={title.synopsis} className="media-detail-synopsis" />
            ) : null}
            {entry.rawStatus || entry.rawListName ? (
              <p className="media-detail-provider-status">Provider status: {entry.rawListName ?? entry.rawStatus}</p>
            ) : null}
          </div>
        </div>
      </div>
    </section>
  );
}

function ProgressRing({ percent }: { percent: number }) {
  return (
    <div
      className="media-detail-progress-ring"
      style={{ '--media-detail-progress': `${percent * 3.6}deg` } as CSSProperties}
      aria-label={`${percent}% complete`}
    >
      <span>{percent}%</span>
    </div>
  );
}

function ProgressStepper({
  label,
  value,
  max,
  onChange,
}: {
  label: string;
  value: number | undefined;
  max?: number;
  onChange: (value: number) => void;
}) {
  const currentValue = clampProgressValue(value ?? 0, max);
  const sliderMax = Math.max(max ?? 100, currentValue, 1);
  const canDecrease = currentValue > 0;
  const canIncrease = max ? currentValue < max : true;
  const setProgressValue = (nextValue: number) => onChange(clampProgressValue(nextValue, max));

  return (
    <div className="media-detail-stepper">
      <div className="media-detail-stepper-row">
        <button
          type="button"
          onClick={() => setProgressValue(currentValue - 1)}
          disabled={!canDecrease}
          aria-label={`Decrease ${label.toLowerCase()}`}
        >
          <Minus aria-hidden />
        </button>
        <input
          type="range"
          min={0}
          max={sliderMax}
          step={1}
          value={currentValue}
          onChange={(event) => setProgressValue(Number(event.target.value))}
          aria-label={`${label} progress`}
        />
        <button
          type="button"
          onClick={() => setProgressValue(currentValue + 1)}
          disabled={!canIncrease}
          aria-label={`Increase ${label.toLowerCase()}`}
        >
          <Plus aria-hidden />
        </button>
      </div>
      <p>{currentValue}{max ? ` / ${max}` : ''}</p>
    </div>
  );
}

function ProgressCockpit(props: Pick<
  MediaEntryDetailContentProps,
  'entry'
  | 'selectedStatus'
  | 'progressEpisodes'
  | 'progressChapters'
  | 'progressVolumes'
  | 'isRefreshingProgress'
  | 'isSavingStatus'
  | 'onSetSelectedStatus'
  | 'onSetProgressEpisodes'
  | 'onSetProgressChapters'
  | 'onSetProgressVolumes'
  | 'onRefreshProgress'
  | 'onSaveStatus'
>) {
  const capabilities = getProgressCapabilities(props.entry.title);
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const percent = getProgressPercent(progressSummary);
  const hasStatusChanged = getEntryStatusChanged(props);

  return (
    <section className="media-detail-progress-card">
      <div className="media-detail-progress-main">
        <ProgressRing percent={percent} />
        <div>
          <p>{progressSummary.label}</p>
          <strong>{progressSummary.value ?? 0}{progressSummary.total ? ` / ${progressSummary.total}` : ''}</strong>
          <span>{getRemainingLabel(progressSummary)}</span>
        </div>
      </div>
      <div className="media-detail-progress-next">
        <div className="media-detail-progress-next-head">
          <div>
            <p>{progressSummary.nextLabel}</p>
            <span>Update progress and sync when the change is ready.</span>
          </div>
          <span className={`media-detail-sync-chip ${hasStatusChanged ? 'media-detail-sync-chip--pending' : 'media-detail-sync-chip--ok'}`}>
            {hasStatusChanged ? <Clock3 aria-hidden /> : <CheckCircle2 aria-hidden />}
            {hasStatusChanged ? 'Unsaved' : 'Synced'}
          </span>
        </div>
        {capabilities.supportsEpisodes ? (
          <ProgressStepper label="Episodes" value={props.progressEpisodes} max={props.entry.title.episodeCount} onChange={props.onSetProgressEpisodes} />
        ) : null}
        {capabilities.supportsChapters ? (
          <ProgressStepper label="Chapters" value={props.progressChapters} max={props.entry.title.chapterCount} onChange={props.onSetProgressChapters} />
        ) : null}
        {capabilities.supportsVolumes ? (
          <ProgressStepper label="Volumes" value={props.progressVolumes} max={props.entry.title.volumeCount} onChange={props.onSetProgressVolumes} />
        ) : null}
      </div>
      <div className="media-detail-score-row">
        <div>
          <p>Your score</p>
          <div aria-label="User score unavailable">
            {[1, 2, 3, 4, 5].map((star) => <Star key={star} aria-hidden />)}
          </div>
        </div>
        <select value={props.selectedStatus} onChange={(event) => props.onSetSelectedStatus(event.target.value)}>
          {NORMALIZED_STATUSES.map((status) => (
            <option key={status.value} value={status.value}>{status.label}</option>
          ))}
        </select>
        <button
          type="button"
          onClick={props.onRefreshProgress}
          disabled={isStatusRefreshDisabled(props.entry.isConnected, props.isSavingStatus, props.isRefreshingProgress)}
          aria-busy={props.isRefreshingProgress}
        >
          <RefreshCcw className={props.isRefreshingProgress ? 'media-detail-spin' : ''} aria-hidden />
          Refresh
        </button>
      </div>
    </section>
  );
}

function ActionRail({
  hasStatusChanged,
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
  onLinkProvider,
}: {
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
  onLinkProvider: () => void;
}) {
  return (
    <div className="media-detail-action-rail">
      <button
        type="button"
        className="media-detail-primary-action"
        onClick={onSaveStatus}
        disabled={isStatusSaveDisabled(hasStatusChanged, isSavingStatus, isRefreshingProgress)}
        aria-busy={isSavingStatus}
      >
        {hasStatusChanged ? <Save aria-hidden /> : <Play aria-hidden />}
        <span>{hasStatusChanged ? getStatusSaveLabel(isSavingStatus) : 'Continue Watching'}</span>
      </button>
      <button type="button" className="media-detail-secondary-action" onClick={onLinkProvider}>
        <Plus aria-hidden />
        Add to Library
      </button>
      <button type="button" className="media-detail-more-action" aria-label="More actions">
        <MoreVertical aria-hidden />
      </button>
    </div>
  );
}

function DetailTabs() {
  return (
    <nav className="media-detail-tabs" aria-label="Media detail sections">
      {DETAIL_TABS.map((tab, index) => (
        <button key={tab} type="button" className={index === 0 ? 'is-active' : undefined}>
          {tab}
        </button>
      ))}
    </nav>
  );
}

function providerLabel(providerId: string) {
  return mediaProviderCatalog.find((provider) => provider.id === providerId)?.name ?? providerId;
}

function availabilityText(availability?: ProviderAvailabilityState) {
  if (!availability || availability.status === 'loading') {
    return 'Checking';
  }

  if (availability.status === 'error') {
    return 'Unavailable';
  }

  return availability.links.length > 0 ? `${availability.links.length} options` : 'Linked';
}

function ProviderSection({
  providerLinks,
  availabilityByProviderLink,
  unlinkingId,
  lastSyncedAt,
  onLinkProvider,
  onUnlink,
}: {
  providerLinks: MediaProviderLinkSummaryDto[];
  availabilityByProviderLink: ProviderAvailabilityMap;
  unlinkingId: string | null;
  lastSyncedAt?: string;
  onLinkProvider: () => void;
  onUnlink: (providerId: string) => void;
}) {
  const visibleLinks = providerLinks.slice(0, 3);
  const hiddenCount = Math.max(providerLinks.length - visibleLinks.length, 0);

  return (
    <section className="media-detail-section">
      <SectionHeading title="Where to Watch" action={providerLinks.length > 3 ? `More ${hiddenCount}+` : undefined} />
      {providerLinks.length === 0 ? (
        <button type="button" className="media-detail-empty-provider" onClick={onLinkProvider}>
          <Plus aria-hidden />
          Link a provider to show availability.
        </button>
      ) : (
        <div className="media-detail-provider-grid">
          {visibleLinks.map((link) => {
            const availability = availabilityByProviderLink[providerAvailabilityKey(link.provider, link.externalId)];
            const catalog = mediaProviderCatalog.find((provider) => provider.id === link.provider);
            return (
              <article key={link.id} className="media-detail-provider-card">
                {catalog ? <MediaProviderIcon providerId={catalog.iconId} aria-hidden /> : <span className="media-detail-provider-letter">{link.provider.slice(0, 1).toUpperCase()}</span>}
                <div>
                  <h4>{providerLabel(link.provider)}</h4>
                  <p>{availabilityText(availability)}</p>
                  {link.externalUrl ? <a href={link.externalUrl} target="_blank" rel="noopener noreferrer">Open</a> : null}
                </div>
                <button type="button" onClick={() => onUnlink(link.provider)} disabled={unlinkingId === link.provider}>
                  {unlinkingId === link.provider ? '...' : 'Unlink'}
                </button>
                <CircleCheck aria-hidden className="media-detail-provider-check" />
              </article>
            );
          })}
          {hiddenCount > 0 ? (
            <button type="button" className="media-detail-provider-more" onClick={onLinkProvider}>
              <MoreVertical aria-hidden />
              More
              <span>{hiddenCount}+</span>
            </button>
          ) : null}
        </div>
      )}
      {lastSyncedAt ? <p className="media-detail-section-note">Last synced {new Date(lastSyncedAt).toLocaleString()}</p> : null}
    </section>
  );
}

function SectionHeading({ title, action }: { title: string; action?: string }) {
  return (
    <div className="media-detail-section-heading">
      <h3>{title}</h3>
      {action ? <button type="button">{action}</button> : null}
    </div>
  );
}

function FranchiseSection({ entry }: { entry: MediaLibraryEntryDetailDto }) {
  const { title } = entry;
  const items = [
    { title: title.canonicalTitle, subtitle: title.episodeCount ? `${title.episodeCount} episodes` : 'Current entry', active: true },
    { title: `${title.canonicalTitle} extras`, subtitle: 'Related media', active: false },
    { title: `${title.canonicalTitle} specials`, subtitle: 'Upcoming', active: false },
  ];

  return (
    <section className="media-detail-section">
      <SectionHeading title="Franchise Order" />
      <div className="media-detail-franchise-strip">
        {items.map((item, index) => (
          <div key={item.title} className="media-detail-franchise-item-wrap">
            <article className={`media-detail-franchise-item ${item.active ? 'is-active' : ''}`}>
              <div className="media-detail-franchise-thumb">
                <DetailArtwork posterUrl={title.posterUrl} title={item.title} />
              </div>
              <div>
                <h4>{item.title}</h4>
                <p>{item.subtitle}</p>
                <span>{item.active ? 'Watched' : 'Linked soon'}</span>
              </div>
            </article>
            {index < items.length - 1 ? <span className="media-detail-franchise-arrow">→</span> : null}
          </div>
        ))}
      </div>
    </section>
  );
}

function CharactersSection({ entry }: { entry: MediaLibraryEntryDetailDto }) {
  const names = ['Main cast', 'Supporting cast', 'Provider roles', 'Watch notes'];

  return (
    <section className="media-detail-section">
      <SectionHeading title="Main Characters" action="See all" />
      <div className="media-detail-character-row">
        {names.map((name, index) => (
          <article key={name} className="media-detail-character-card">
            <div>
              <DetailArtwork posterUrl={entry.title.posterUrl} title={name} />
            </div>
            <button type="button" aria-label={`Favorite ${name}`}>
              <Heart aria-hidden />
            </button>
            <h4>{index === 0 ? entry.title.canonicalTitle : name}</h4>
            <p>{index === 0 ? 'Main' : 'Supporting'}</p>
            <Info aria-hidden className="media-detail-character-info" />
          </article>
        ))}
      </div>
    </section>
  );
}

function CommunitySection({ entry, progressSummary }: { entry: MediaLibraryEntryDetailDto; progressSummary: ProgressSummary }) {
  return (
    <section className="media-detail-community">
      <SectionHeading title="Community" action="See all" />
      <div className="media-detail-community-grid">
        <MetricCard icon={<Star aria-hidden />} label="Progress" value={`${getProgressPercent(progressSummary)}%`} detail={getRemainingLabel(progressSummary)} />
        <MetricCard icon={<Heart aria-hidden />} label="Library" value={entry.isConnected ? 'Synced' : 'Local'} detail={providerLabel(entry.provider)} />
        <MetricCard icon={<Flame aria-hidden />} label="Status" value={progressKindLabel(entry.title)} detail={mediaKindLabel(entry.title.mediaKind)} />
      </div>
    </section>
  );
}

function InformationSection({ entry }: { entry: MediaLibraryEntryDetailDto }) {
  const { title } = entry;
  const rows = [
    ['Format', progressKindLabel(title)],
    ['Status', releaseStatusLabel(title.releaseStatusDimension)],
    ['Aired', title.startYear ? String(title.startYear) : 'Unknown'],
    ['Provider', providerLabel(entry.provider)],
    ['Progress', getPrimaryProgressSummary(title, entry.progressEpisodes, entry.progressChapters, entry.progressVolumes).total ? `${getPrimaryProgressSummary(title, entry.progressEpisodes, entry.progressChapters, entry.progressVolumes).total} ${getPrimaryProgressSummary(title, entry.progressEpisodes, entry.progressChapters, entry.progressVolumes).noun}` : 'Unknown'],
    ['Rating', mediaKindLabel(title.mediaKind)],
  ];

  return (
    <section className="media-detail-info-card">
      <h3>Information</h3>
      <dl>
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function MetricCard({ icon, label, value, detail }: { icon: ReactNode; label: string; value: string; detail: string }) {
  return (
    <article className="media-detail-metric-card">
      <p>{label}</p>
      <strong>{icon}{value}</strong>
      <span>{detail}</span>
    </article>
  );
}

function MediaEntryDetailContent(props: MediaEntryDetailContentProps) {
  const mediaKind = props.entry.title.mediaKind;
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const hasStatusChanged = getEntryStatusChanged(props);

  return (
    <>
      <DetailPageLayout embedded={props.embedded}>
        <div className="media-detail-layout">
          <section className="media-detail-top-area">
            <MediaHero entry={props.entry} progressSummary={progressSummary} onNavigateBack={props.onNavigateBack} />
            <div className="media-detail-desktop-progress">
              <ProgressCockpit {...props} />
            </div>
            <div className="media-detail-mobile-progress">
              <ProgressCockpit {...props} />
            </div>
          </section>
          <div className="media-detail-main-column">
            <ActionRail
              hasStatusChanged={hasStatusChanged}
              isSavingStatus={props.isSavingStatus}
              isRefreshingProgress={props.isRefreshingProgress}
              onSaveStatus={props.onSaveStatus}
              onLinkProvider={() => props.onSetShowLinkDialog(true)}
            />
            <section className="media-detail-overview-card">
              <DetailTabs />
              <ProviderSection
                providerLinks={props.entry.providerLinks}
                availabilityByProviderLink={props.availabilityByProviderLink}
                unlinkingId={props.unlinkingId}
                lastSyncedAt={props.entry.lastSyncedAt}
                onLinkProvider={() => props.onSetShowLinkDialog(true)}
                onUnlink={props.onUnlink}
              />
              <FranchiseSection entry={props.entry} />
              <CharactersSection entry={props.entry} />
              <div className="media-detail-overview-meta-grid">
                <InformationSection entry={props.entry} />
                <CommunitySection entry={props.entry} progressSummary={progressSummary} />
              </div>
            </section>
          </div>
        </div>
      </DetailPageLayout>
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

export function MediaEntryDetailPage({
  libraryEntryId,
  onNavigateBack,
  embedded = false,
  onHeadingChange,
}: MediaEntryDetailPageProps) {
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

  useEffect(() => {
    if (!entry || !onHeadingChange) {
      return;
    }

    onHeadingChange({
      eyebrow: 'Cantaro · Media',
      title: entry.title.canonicalTitle,
      details: [mediaKindLabel(entry.title.mediaKind), entry.isConnected ? 'Synced' : 'Not synced'],
      hidden: true,
    });
  }, [entry, onHeadingChange]);

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
