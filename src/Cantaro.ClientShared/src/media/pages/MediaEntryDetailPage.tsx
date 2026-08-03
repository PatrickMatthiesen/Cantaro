import { useCallback, useEffect, useRef, useState, type CSSProperties, type Dispatch, type ReactNode, type SetStateAction } from 'react';
import {
  CalendarDays,
  CheckCircle2,
  CircleCheck,
  Clock3,
  ExternalLink,
  Flame,
  Heart,
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
  MediaContinueWatchingDto,
  MediaEpisodeCatalogDto,
  MediaEpisodeDestinationDto,
  MediaProviderCharacterCreditDto,
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

const DETAIL_TABS = [
  { id: 'overview', label: 'Overview' },
  { id: 'episodes', label: 'Episodes' },
  { id: 'progress', label: 'Progress' },
  { id: 'providers', label: 'Providers' },
  { id: 'franchise', label: 'Franchise' },
  { id: 'characters', label: 'Characters' },
  { id: 'details', label: 'Details' },
] as const;

type DetailTabId = (typeof DETAIL_TABS)[number]['id'];

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

type ContinueWatchingState =
  | { status: 'loading' }
  | { status: 'loaded'; value: MediaContinueWatchingDto }
  | { status: 'error' };

type EpisodeCatalogState =
  | { status: 'loading' }
  | { status: 'loaded'; value: MediaEpisodeCatalogDto }
  | { status: 'error' };

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
  continueWatching: ContinueWatchingState;
  episodeCatalog: EpisodeCatalogState;
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
  onReloadEpisodes: () => void;
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
    next[key] = current[key] ?? { status: 'loading', links: [], characters: [] };
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
          characters: details.characters ?? [],
        },
      };
    } catch (error) {
      return {
        key,
        state: {
          status: 'error' as const,
          links: [],
          characters: [],
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

function getEpisodeCatalogRevision(state: EpisodeCatalogState): string {
  if (state.status !== 'loaded') return state.status;
  return state.value.episodes
    .map((episode) => `${episode.episodeNumber}:${episode.url ?? ''}:${episode.hasConflict}`)
    .join('|');
}

function useContinueWatching(
  entry: MediaLibraryEntryDetailDto | null,
  episodeCatalog: EpisodeCatalogState,
): ContinueWatchingState {
  const [state, setState] = useState<ContinueWatchingState>({ status: 'loading' });
  const entryId = entry?.id;
  const persistedProgress = entry?.progressEpisodes;
  const episodeCatalogRevision = getEpisodeCatalogRevision(episodeCatalog);

  useEffect(() => {
    if (!entryId || episodeCatalog.status === 'loading') {
      setState({ status: 'loading' });
      return;
    }

    let isCancelled = false;
    setState({ status: 'loading' });

    void mediaApi.getContinueWatching(entryId)
      .then((value) => {
        if (!isCancelled) setState({ status: 'loaded', value });
      })
      .catch(() => {
        if (!isCancelled) setState({ status: 'error' });
      });

    return () => {
      isCancelled = true;
    };
  }, [entryId, persistedProgress, episodeCatalog.status, episodeCatalogRevision]);

  return state;
}

function useEpisodeCatalog(entry: MediaLibraryEntryDetailDto | null) {
  const [state, setState] = useState<EpisodeCatalogState>({ status: 'loading' });
  const entryId = entry?.id;

  const reload = useCallback(() => {
    if (!entryId) {
      setState({ status: 'loading' });
      return;
    }

    setState({ status: 'loading' });
    void mediaApi.getEpisodes(entryId)
      .then((value) => setState({ status: 'loaded', value }))
      .catch(() => setState({ status: 'error' }));
  }, [entryId]);

  useEffect(() => {
    reload();
  }, [reload]);

  return { state, reload };
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

function SaveProgressAction({
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
}: {
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
}) {
  return (
    <button
      type="button"
      className="media-detail-primary-action"
      onClick={onSaveStatus}
      disabled={isSavingStatus || isRefreshingProgress}
      aria-busy={isSavingStatus}
    >
      <Save aria-hidden />
      <span>{getStatusSaveLabel(isSavingStatus)}</span>
    </button>
  );
}

function getContinueWatchingLabel(state: ContinueWatchingState): string {
  if (state.status === 'loading') return 'Finding episode...';
  if (state.status === 'error') return "Couldn't load Crunchyroll link";

  const labels: Record<Exclude<MediaContinueWatchingDto['outcome'], 'direct'>, string> = {
    series_fallback: 'Open series on Crunchyroll',
    completed: 'Completed',
    conflict: 'Episode link needs review',
    unavailable: 'Crunchyroll link not observed yet',
  };
  return state.value.outcome === 'direct'
    ? `Continue episode ${state.value.episodeNumber ?? ''}`.trim()
    : labels[state.value.outcome];
}

function getContinueDestination(state: ContinueWatchingState): MediaContinueWatchingDto | null {
  if (state.status !== 'loaded') return null;
  if (state.value.outcome === 'direct') return state.value;
  if (state.value.outcome === 'series_fallback') return state.value;
  return null;
}

function normalizeCrunchyrollSeriesUrl(value?: string): string | null {
  if (!value) return null;

  try {
    const url = new URL(value);
    const isCrunchyroll = url.hostname === 'crunchyroll.com' || url.hostname === 'www.crunchyroll.com';
    const isSeriesPath = /^\/series\/[A-Z0-9]+(?:\/[^/?#]+)?\/?$/i.test(url.pathname);
    if (url.protocol !== 'https:' || !isCrunchyroll || !isSeriesPath) return null;
    return `https://www.crunchyroll.com${url.pathname}`;
  } catch {
    return null;
  }
}

function getCrunchyrollSeriesUrl(availabilityByProviderLink: ProviderAvailabilityMap): string | null {
  for (const state of Object.values(availabilityByProviderLink)) {
    if (state.status !== 'loaded') continue;
    const link = state.links.find((candidate) => candidate.serviceId.toLowerCase() === 'crunchyroll');
    const url = link ? normalizeCrunchyrollSeriesUrl(link.url) : null;
    if (url) return url;
  }

  return null;
}

function selectContinueUrl(destination: MediaContinueWatchingDto | null, seriesUrl: string | null) {
  if (destination?.url) return destination.url;
  return destination?.outcome === 'series_fallback' ? seriesUrl : null;
}

function ContinueDestinationLink({
  state,
  destination,
  url,
}: {
  state: ContinueWatchingState;
  destination: MediaContinueWatchingDto | null;
  url: string;
}) {
  const isEpisodeLink = destination?.outcome === 'direct';
  const label = isEpisodeLink ? getContinueWatchingLabel(state) : 'Open series on Crunchyroll';

  return (
    <a
      className="media-detail-primary-action"
      href={url}
      target="_blank"
      rel="noopener noreferrer"
    >
      {isEpisodeLink ? <Play aria-hidden /> : <ExternalLink aria-hidden />}
      <span>{label}</span>
    </a>
  );
}

function ContinueUnavailableAction({ state }: { state: ContinueWatchingState }) {
  return (
    <button type="button" className="media-detail-primary-action" disabled>
      <Play aria-hidden />
      <span>{getContinueWatchingLabel(state)}</span>
    </button>
  );
}

const UPCOMING_RELEASE_OUTCOMES = new Set<MediaContinueWatchingDto['outcome']>([
  'series_fallback',
  'unavailable',
]);

function isFutureRelease(timestamp?: string): timestamp is string {
  return Boolean(timestamp && Date.parse(timestamp) > Date.now());
}

function readReleaseEpisodeNumber(label?: string): number | null {
  const value = /\b(?:episode|ep|e)\s*(\d+)\b/i.exec(label ?? '')?.[1];
  return value ? Number(value) : null;
}

function getUpcomingRelease(
  state: ContinueWatchingState,
  nextReleaseAt?: string,
  nextReleaseLabel?: string,
) {
  if (state.status !== 'loaded') return null;
  if (!UPCOMING_RELEASE_OUTCOMES.has(state.value.outcome)) return null;
  if (!isFutureRelease(nextReleaseAt)) return null;

  const releaseEpisodeNumber = readReleaseEpisodeNumber(nextReleaseLabel);
  if (releaseEpisodeNumber !== null && releaseEpisodeNumber !== state.value.episodeNumber) return null;

  return formatNextReleaseDisplay(nextReleaseAt);
}

function ContinueWatchingAction({
  state,
  seriesUrl,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  state: ContinueWatchingState;
  seriesUrl: string | null;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  const upcomingRelease = getUpcomingRelease(state, nextReleaseAt, nextReleaseLabel);
  if (upcomingRelease) {
    return (
      <button
        type="button"
        className="media-detail-primary-action"
        disabled
        title={`${nextReleaseLabel ?? 'Next episode'} expected ${upcomingRelease.absolute}`}
      >
        <Clock3 aria-hidden />
        <span>Come back {upcomingRelease.relative}</span>
      </button>
    );
  }

  const destination = getContinueDestination(state);
  const url = selectContinueUrl(destination, seriesUrl);
  if (url) return <ContinueDestinationLink state={state} destination={destination} url={url} />;

  return <ContinueUnavailableAction state={state} />;
}

function ActionRail({
  hasStatusChanged,
  isSavingStatus,
  isRefreshingProgress,
  onSaveStatus,
  onLinkProvider,
  continueWatching,
  crunchyrollSeriesUrl,
  nextReleaseAt,
  nextReleaseLabel,
}: {
  hasStatusChanged: boolean;
  isSavingStatus: boolean;
  isRefreshingProgress: boolean;
  onSaveStatus: () => void;
  onLinkProvider: () => void;
  continueWatching: ContinueWatchingState;
  crunchyrollSeriesUrl: string | null;
  nextReleaseAt?: string;
  nextReleaseLabel?: string;
}) {
  return (
    <div className="media-detail-action-rail">
      {hasStatusChanged ? (
        <SaveProgressAction
          isSavingStatus={isSavingStatus}
          isRefreshingProgress={isRefreshingProgress}
          onSaveStatus={onSaveStatus}
        />
      ) : (
        <ContinueWatchingAction
          state={continueWatching}
          seriesUrl={crunchyrollSeriesUrl}
          nextReleaseAt={nextReleaseAt}
          nextReleaseLabel={nextReleaseLabel}
        />
      )}
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

function DetailTabs({ activeTab, onChange }: { activeTab: DetailTabId; onChange: (tab: DetailTabId) => void }) {
  return (
    <nav className="media-detail-tabs" aria-label="Media detail sections" role="tablist">
      {DETAIL_TABS.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          id={`media-detail-tab-${tab.id}`}
          aria-controls={`media-detail-panel-${tab.id}`}
          aria-selected={activeTab === tab.id}
          className={activeTab === tab.id ? 'is-active' : undefined}
          onClick={() => onChange(tab.id)}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  );
}

function getEpisodeRows(entry: MediaLibraryEntryDetailDto, episodes: MediaEpisodeDestinationDto[]) {
  const knownNumbers = episodes.map((episode) => episode.episodeNumber);
  const episodeCount = entry.title.episodeCount ?? Math.max(0, ...knownNumbers);
  const numbers = episodeCount > 0 && episodeCount <= 100
    ? Array.from({ length: episodeCount }, (_, index) => index + 1)
    : [...new Set([...knownNumbers, (entry.progressEpisodes ?? 0) + 1])].sort((left, right) => left - right);
  const episodesByNumber = new Map(episodes.map((episode) => [episode.episodeNumber, episode]));

  return numbers.map((episodeNumber) => ({
    episodeNumber,
    destination: episodesByNumber.get(episodeNumber),
  }));
}

function getEpisodeDestinationLabel(destination?: MediaEpisodeDestinationDto) {
  if (destination?.hasConflict) return 'Needs review';
  if (destination?.url) return 'Link available';
  return 'Not observed yet';
}

function getEpisodeProgressLabel(episodeNumber: number, watchedThrough: number, fallback: string) {
  if (episodeNumber <= watchedThrough) return 'Watched';
  if (episodeNumber === watchedThrough + 1) return 'Up next';
  return fallback;
}

function EpisodeDestinationAction({
  episodeNumber,
  destination,
  stateLabel,
}: {
  episodeNumber: number;
  destination?: MediaEpisodeDestinationDto;
  stateLabel: string;
}) {
  if (!destination?.url || destination.hasConflict) {
    return <span className="media-detail-episode-state">{stateLabel}</span>;
  }

  return (
    <a href={destination.url} target="_blank" rel="noopener noreferrer" aria-label={`Open episode ${episodeNumber} on Crunchyroll`}>
      <Play aria-hidden />
      Watch
    </a>
  );
}

function EpisodeRow({
  episodeNumber,
  destination,
  watchedThrough,
}: {
  episodeNumber: number;
  destination?: MediaEpisodeDestinationDto;
  watchedThrough: number;
}) {
  const isNext = episodeNumber === watchedThrough + 1;
  const stateLabel = getEpisodeDestinationLabel(destination);
  const progressLabel = getEpisodeProgressLabel(episodeNumber, watchedThrough, stateLabel);

  return (
    <li className={`media-detail-episode-row ${isNext ? 'is-next' : ''}`}>
      <div className="media-detail-episode-number" aria-hidden>{episodeNumber}</div>
      <div className="media-detail-episode-copy">
        <strong>{destination?.title || `Episode ${episodeNumber}`}</strong>
        <span>{progressLabel}</span>
      </div>
      <EpisodeDestinationAction episodeNumber={episodeNumber} destination={destination} stateLabel={stateLabel} />
    </li>
  );
}

function EpisodeSectionHeading({
  state,
  catalog,
  availableCount,
  onRefresh,
}: {
  state: EpisodeCatalogState;
  catalog: MediaEpisodeCatalogDto | null;
  availableCount: number;
  onRefresh: () => void;
}) {
  const isLoading = state.status === 'loading';
  const summary = state.status === 'loaded'
    ? `${availableCount} episode links collected`
    : 'Loading collected episode links…';

  return (
    <div className="media-detail-episodes-heading">
      <div>
        <h3>Episodes</h3>
        <p>{summary}</p>
      </div>
      <div className="media-detail-episodes-actions">
        <button type="button" onClick={onRefresh} disabled={isLoading}>
          <RefreshCcw className={isLoading ? 'media-detail-spin' : ''} aria-hidden />
          Refresh links
        </button>
        {catalog?.seriesUrl ? (
          <a href={catalog.seriesUrl} target="_blank" rel="noopener noreferrer">
            <ExternalLink aria-hidden />
            Open series on Crunchyroll
          </a>
        ) : null}
      </div>
    </div>
  );
}

function EpisodeSectionContent({
  entry,
  state,
  rows,
  onRefresh,
}: {
  entry: MediaLibraryEntryDetailDto;
  state: EpisodeCatalogState;
  rows: ReturnType<typeof getEpisodeRows>;
  onRefresh: () => void;
}) {
  if (state.status === 'error') {
    return (
      <div className="media-detail-episode-empty" role="alert">
        <p>Episode links could not be loaded.</p>
        <button type="button" onClick={onRefresh}>Try again</button>
      </div>
    );
  }

  if (state.status === 'loaded' && rows.length === 0) {
    return (
      <div className="media-detail-episode-empty">
        <p>No episode URLs have been observed for this title yet.</p>
        <span>Visit its Crunchyroll series page with the Cantaro extension enabled, then refresh this tab.</span>
      </div>
    );
  }

  if (rows.length === 0) return null;

  return (
    <ol className="media-detail-episode-list">
      {rows.map((row) => (
        <EpisodeRow
          key={row.episodeNumber}
          episodeNumber={row.episodeNumber}
          destination={row.destination}
          watchedThrough={entry.progressEpisodes ?? 0}
        />
      ))}
    </ol>
  );
}

function getEpisodeSectionData(
  entry: MediaLibraryEntryDetailDto,
  state: EpisodeCatalogState,
  fallbackSeriesUrl: string | null,
) {
  if (state.status !== 'loaded') {
    return { catalog: null, rows: [], availableCount: 0 };
  }

  const catalog = {
    ...state.value,
    seriesUrl: state.value.seriesUrl ?? fallbackSeriesUrl ?? undefined,
  };
  return {
    catalog,
    rows: getEpisodeRows(entry, catalog.episodes),
    availableCount: catalog.episodes.filter((episode) => episode.url && !episode.hasConflict).length,
  };
}

function EpisodesSection({
  entry,
  state,
  seriesUrl,
  onRefresh,
}: {
  entry: MediaLibraryEntryDetailDto;
  state: EpisodeCatalogState;
  seriesUrl: string | null;
  onRefresh: () => void;
}) {
  const { catalog, rows, availableCount } = getEpisodeSectionData(entry, state, seriesUrl);

  return (
    <section
      className="media-detail-section media-detail-episodes"
      role="tabpanel"
      id="media-detail-panel-episodes"
      aria-labelledby="media-detail-tab-episodes"
    >
      <EpisodeSectionHeading
        state={state}
        catalog={catalog}
        availableCount={availableCount}
        onRefresh={onRefresh}
      />
      <EpisodeSectionContent entry={entry} state={state} rows={rows} onRefresh={onRefresh} />
    </section>
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
      <SectionHeading title="Linked providers" action={providerLinks.length > 3 ? `More ${hiddenCount}+` : undefined} />
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

interface CharactersSectionProps {
  entry: MediaLibraryEntryDetailDto;
  availabilityByProviderLink: ProviderAvailabilityMap;
}

interface CharacterSectionData {
  characters: MediaProviderCharacterCreditDto[];
  message: string | null;
  isError: boolean;
}

function getCharacterSectionMessage(
  supportedLinkCount: number,
  states: Array<ProviderAvailabilityState | undefined>,
  characterCount: number,
): string | null {
  if (supportedLinkCount === 0) return 'Character credits are not supported by the linked providers.';
  if (states.some((state) => !state || state.status === 'loading')) return 'Loading character credits…';
  if (states.every((state) => state?.status === 'error')) return 'Character credits could not be loaded from AniList.';
  if (characterCount === 0) return 'AniList has no character credits for this title.';
  return null;
}

function getCharacterSectionData({ entry, availabilityByProviderLink }: CharactersSectionProps): CharacterSectionData {
  const supportedLinks = entry.providerLinks.filter((link) => link.provider === 'anilist');
  const states = supportedLinks.map((link) => availabilityByProviderLink[providerAvailabilityKey(link.provider, link.externalId)]);
  const characters = states
    .filter((state): state is ProviderAvailabilityState => state?.status === 'loaded')
    .flatMap((state) => state.characters)
    .sort((left, right) => left.order - right.order);

  return {
    characters,
    message: getCharacterSectionMessage(supportedLinks.length, states, characters.length),
    isError: states.some((state) => state?.status === 'error'),
  };
}

function CharacterCard({ character }: { character: MediaProviderCharacterCreditDto }) {
  const name = character.providerUrl
    ? <a href={character.providerUrl} target="_blank" rel="noreferrer">{character.name}</a>
    : character.name;

  return (
    <article className="media-detail-character-card">
      <div><DetailArtwork posterUrl={character.imageUrl} title={character.name} /></div>
      <h4>{name}</h4>
      <p>{character.role === 'main' ? 'Main' : 'Supporting'}</p>
    </article>
  );
}

function CharacterList({ characters }: { characters: MediaProviderCharacterCreditDto[] }) {
  if (characters.length === 0) return null;

  return (
    <div className="media-detail-character-row">
      {characters.map((character) => <CharacterCard key={character.characterId} character={character} />)}
    </div>
  );
}

function CharactersSection(props: CharactersSectionProps) {
  const [showAll, setShowAll] = useState(false);
  const data = getCharacterSectionData(props);
  const visibleCharacters = showAll ? data.characters : data.characters.slice(0, 8);

  return (
    <section className="media-detail-section">
      <SectionHeading title="Characters" />
      {data.message ? <p className="media-detail-character-state" role={data.isError ? 'alert' : undefined}>{data.message}</p> : null}
      <CharacterList characters={visibleCharacters} />
      {data.characters.length > 8 ? (
        <button type="button" className="media-detail-character-more" onClick={() => setShowAll((current) => !current)}>
          {showAll ? 'Show primary characters' : `View all ${data.characters.length} characters`}
        </button>
      ) : null}
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

function MediaDetailTabPanel({
  activeTab,
  props,
  progressSummary,
  crunchyrollSeriesUrl,
}: {
  activeTab: DetailTabId;
  props: MediaEntryDetailContentProps;
  progressSummary: ProgressSummary;
  crunchyrollSeriesUrl: string | null;
}) {
  const panels: Record<DetailTabId, ReactNode> = {
    overview: (
      <div role="tabpanel" id="media-detail-panel-overview" aria-labelledby="media-detail-tab-overview">
        <FranchiseSection entry={props.entry} />
        <CharactersSection entry={props.entry} availabilityByProviderLink={props.availabilityByProviderLink} />
        <div className="media-detail-overview-meta-grid">
          <InformationSection entry={props.entry} />
          <CommunitySection entry={props.entry} progressSummary={progressSummary} />
        </div>
      </div>
    ),
    episodes: (
      <EpisodesSection
        entry={props.entry}
        state={props.episodeCatalog}
        seriesUrl={crunchyrollSeriesUrl}
        onRefresh={props.onReloadEpisodes}
      />
    ),
    progress: (
      <div role="tabpanel" id="media-detail-panel-progress" aria-labelledby="media-detail-tab-progress">
        <CommunitySection entry={props.entry} progressSummary={progressSummary} />
      </div>
    ),
    providers: (
      <div role="tabpanel" id="media-detail-panel-providers" aria-labelledby="media-detail-tab-providers">
        <ProviderSection
          providerLinks={props.entry.providerLinks}
          availabilityByProviderLink={props.availabilityByProviderLink}
          unlinkingId={props.unlinkingId}
          lastSyncedAt={props.entry.lastSyncedAt}
          onLinkProvider={() => props.onSetShowLinkDialog(true)}
          onUnlink={props.onUnlink}
        />
      </div>
    ),
    franchise: (
      <div role="tabpanel" id="media-detail-panel-franchise" aria-labelledby="media-detail-tab-franchise">
        <FranchiseSection entry={props.entry} />
      </div>
    ),
    characters: (
      <div role="tabpanel" id="media-detail-panel-characters" aria-labelledby="media-detail-tab-characters">
        <CharactersSection entry={props.entry} availabilityByProviderLink={props.availabilityByProviderLink} />
      </div>
    ),
    details: (
      <div role="tabpanel" id="media-detail-panel-details" aria-labelledby="media-detail-tab-details">
        <InformationSection entry={props.entry} />
      </div>
    ),
  };

  return panels[activeTab];
}

function MediaEntryDetailContent(props: MediaEntryDetailContentProps) {
  const [activeTab, setActiveTab] = useState<DetailTabId>('overview');
  const mediaKind = props.entry.title.mediaKind;
  const progressSummary = getPrimaryProgressSummary(
    props.entry.title,
    props.progressEpisodes,
    props.progressChapters,
    props.progressVolumes,
  );
  const hasStatusChanged = getEntryStatusChanged(props);
  const crunchyrollSeriesUrl = getCrunchyrollSeriesUrl(props.availabilityByProviderLink);

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
              continueWatching={props.continueWatching}
              crunchyrollSeriesUrl={crunchyrollSeriesUrl}
              nextReleaseAt={props.entry.nextReleaseAt}
              nextReleaseLabel={props.entry.nextReleaseLabel}
            />
            <section className="media-detail-overview-card">
              <DetailTabs activeTab={activeTab} onChange={setActiveTab} />
              <MediaDetailTabPanel
                activeTab={activeTab}
                props={props}
                progressSummary={progressSummary}
                crunchyrollSeriesUrl={crunchyrollSeriesUrl}
              />
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
  const { state: episodeCatalog, reload: reloadEpisodes } = useEpisodeCatalog(entry);
  const continueWatching = useContinueWatching(entry, episodeCatalog);
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
      continueWatching={continueWatching}
      episodeCatalog={episodeCatalog}
      onSetShowLinkDialog={setShowLinkDialog}
      onSetProgressEpisodes={setProgressEpisodes}
      onSetProgressChapters={setProgressChapters}
      onSetProgressVolumes={setProgressVolumes}
      onSetSelectedStatus={setSelectedStatus}
      onRefreshProgress={() => void handleRefreshFromProvider()}
      onSaveStatus={() => void handleSaveStatus()}
      onUnlink={(providerId) => void handleUnlink(providerId)}
      onReloadEpisodes={reloadEpisodes}
    />
  );
}
