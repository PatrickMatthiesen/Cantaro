import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import {
  getProviderTitleDetails,
  isAvailabilityStale,
  isAvailabilityUnavailable,
  providerAvailabilityKey,
  readAvailabilityMetadata,
  type ProviderAvailabilityMap,
} from '../../components/media-entry-detail/providerAvailability';
import { type ShowSnackbar, type SnackbarNotification } from '../../../ui';
import { mediaApi } from '../../services/mediaApi';
import { mainMediaProviderId } from '../../services/mediaProviders';
import {
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../../services/mediaRefreshCache';
import type {
  MediaEntryDetailModel,
  MediaLibraryImportEventDto,
  MediaProviderLinkSummaryDto,
  MediaProviderTitleDetailsDto,
  MediaTitleDetailDto,
  MediaViewerProviderBindingDto,
  MediaViewerStateDto,
} from '../../services/mediaApi';
import type { ContinueWatchingState, EpisodeCatalogState, StatusDraft } from './mediaEntryDetailTypes';

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

async function loadAvailabilityStates(
  providerLinks: MediaProviderLinkSummaryDto[],
  forceRefresh = false,
) {
  return Promise.all(providerLinks.map(async (link) => {
    const key = providerAvailabilityKey(link.provider, link.externalId);

    try {
      const details = await getProviderTitleDetails(link, forceRefresh);
      return { key, state: buildLoadedAvailabilityState(details) };
    } catch (error) {
      return { key, state: buildFailedAvailabilityState(link, error) };
    }
  }));
}

function buildLoadedAvailabilityState(details: MediaProviderTitleDetailsDto) {
  const metadata = readAvailabilityMetadata(details);
  const links = details.availabilityLinks ?? [];
  const confirmedNoLinks = metadata.status === 'fresh' && links.length === 0;
  return {
    status: (isAvailabilityUnavailable(details) || confirmedNoLinks)
      ? 'unavailable' as const
      : 'loaded' as const,
    links,
    characters: details.characters ?? [],
    isStale: isAvailabilityStale(details),
    refreshedAt: metadata.refreshedAt,
  };
}

function buildFailedAvailabilityState(link: MediaProviderLinkSummaryDto, error: unknown) {
  const links = link.availabilityLinks ?? [];
  return {
    status: 'error' as const,
    links,
    characters: [],
    isStale: links.length > 0,
    refreshedAt: link.availabilityLastVerifiedAt,
    error: getErrorMessage(error, 'Failed to load availability'),
  };
}

export function useTimedSnackbar(timeoutMs = 3000) {
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

function getPrimaryBinding(viewer: MediaViewerStateDto | null): MediaViewerProviderBindingDto | undefined {
  if (!viewer) return undefined;
  return viewer.providerBindings.find((binding) => binding.isConnected)
    ?? viewer.providerBindings[0];
}

function composeViewerProjection(viewer: MediaViewerStateDto | null) {
  if (!viewer) {
    return {
      id: '',
      isInLibrary: false,
      status: 'planned',
      score: null,
      providerListNames: [] as string[],
      isConnected: false,
    };
  }

  return {
    id: viewer.id,
    isInLibrary: true,
    status: viewer.status,
    score: viewer.score ?? null,
    providerListNames: viewer.providerBindings.flatMap((binding) => binding.providerListNames),
    progressEpisodes: viewer.progressEpisodes,
    progressChapters: viewer.progressChapters,
    progressVolumes: viewer.progressVolumes,
    isConnected: viewer.providerBindings.some((binding) => binding.isConnected),
    updatedAt: viewer.updatedAt,
  };
}

function composeProviderProjection(
  title: MediaTitleDetailDto,
  primaryBinding: MediaViewerProviderBindingDto | undefined,
) {
  if (primaryBinding) {
    return {
      provider: primaryBinding.provider,
      providerMediaId: primaryBinding.providerMediaId,
      lastSyncedAt: primaryBinding.lastSyncedAt,
      lastRemoteUpdateAt: primaryBinding.lastRemoteUpdateAt,
    };
  }

  const titleProvider = title.providerLinks[0];
  return {
    provider: titleProvider?.provider ?? '',
    providerMediaId: titleProvider?.externalId ?? '',
  };
}

function composeEntry(
  title: MediaTitleDetailDto,
  viewer: MediaViewerStateDto | null,
  viewerStateStatus: MediaEntryDetailModel['viewerStateStatus'] = 'loaded',
): MediaEntryDetailModel {
  const primaryBinding = getPrimaryBinding(viewer);
  const viewerProjection = composeViewerProjection(viewer);
  const providerProjection = composeProviderProjection(title, primaryBinding);
  return {
    ...viewerProjection,
    ...providerProjection,
    mediaTitleId: title.id,
    viewerStateStatus,
    title,
    nextReleaseAt: title.nextReleaseAt,
    nextReleaseLabel: title.nextReleaseLabel,
    updatedAt: viewerProjection.updatedAt ?? title.updatedAt,
    providerLinks: title.providerLinks,
  };
}

async function loadComposedEntry(mediaTitleId: string) {
  const [title, viewer] = await Promise.all([
    mediaApi.getMediaTitle(mediaTitleId),
    mediaApi.getViewerState(mediaTitleId),
  ]);
  return composeEntry(title, viewer, 'loaded');
}

export function useEntryDetailState(mediaTitleId: string) {
  const [entry, setEntry] = useState<MediaEntryDetailModel | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [progressEpisodes, setProgressEpisodes] = useState<number | undefined>();
  const [progressChapters, setProgressChapters] = useState<number | undefined>();
  const [progressVolumes, setProgressVolumes] = useState<number | undefined>();
  const [selectedStatus, setSelectedStatus] = useState('');

  const applyEntryData = useCallback((data: MediaEntryDetailModel) => {
    setEntry(data);
    setProgressEpisodes(data.progressEpisodes);
    setProgressChapters(data.progressChapters);
    setProgressVolumes(data.progressVolumes);
    setSelectedStatus(data.status);
  }, []);

  const loadEntry = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      const viewerPromise = mediaApi.getViewerState(mediaTitleId)
        .then((viewer) => ({ viewer, failed: false as const }))
        .catch(() => ({ viewer: null, failed: true as const }));
      const title = await mediaApi.getMediaTitle(mediaTitleId);
      applyEntryData(composeEntry(title, null, 'loading'));
      setIsLoading(false);

      const viewerResult = await viewerPromise;
      applyEntryData(composeEntry(
        title,
        viewerResult.viewer,
        viewerResult.failed ? 'error' : 'loaded',
      ));
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load entry'));
      setIsLoading(false);
    }
  }, [applyEntryData, mediaTitleId]);

  const reloadEntry = useCallback(async () => {
    const data = await loadComposedEntry(mediaTitleId);
    applyEntryData(data);
    return data;
  }, [applyEntryData, mediaTitleId]);

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

export function useProviderAvailability(entry: MediaEntryDetailModel | null) {
  const [availabilityByProviderLink, setAvailabilityByProviderLink] = useState<ProviderAvailabilityMap>({});
  const [refreshRevision, setRefreshRevision] = useState(0);
  const forceRefreshRef = useRef(false);

  const reload = useCallback(() => {
    forceRefreshRef.current = true;
    setRefreshRevision((revision) => revision + 1);
  }, []);

  useEffect(() => {
    if (!entry || entry.providerLinks.length === 0) {
      setAvailabilityByProviderLink({});
      return;
    }

    let isCancelled = false;
    const forceRefresh = forceRefreshRef.current;
    forceRefreshRef.current = false;
    setAvailabilityByProviderLink((current) => buildAvailabilityMap(entry.providerLinks, current));

    const loadAvailability = async () => {
      const results = await loadAvailabilityStates(entry.providerLinks, forceRefresh);
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
  }, [entry, refreshRevision]);

  return { availabilityByProviderLink, reload };
}

function getEpisodeCatalogRevision(state: EpisodeCatalogState): string {
  if (state.status !== 'loaded') return state.status;
  return state.value.episodes
    .map((episode) => `${episode.episodeNumber}:${episode.destinations
      .map((destination) => `${destination.serviceId}:${destination.url}`)
      .join(',')}:${episode.hasConflict}`)
    .join('|');
}

export function useContinueWatching(
  entry: MediaEntryDetailModel | null,
  episodeCatalog: EpisodeCatalogState,
): ContinueWatchingState {
  const [state, setState] = useState<ContinueWatchingState>({ status: 'loading' });
  const mediaTitleId = entry?.mediaTitleId;
  const isInLibrary = entry?.isInLibrary;
  const persistedProgress = entry?.progressEpisodes;
  const episodeCatalogRevision = getEpisodeCatalogRevision(episodeCatalog);

  useEffect(() => {
    if (!mediaTitleId || !isInLibrary || episodeCatalog.status === 'loading') {
      setState({ status: 'loading' });
      return;
    }

    let isCancelled = false;
    setState({ status: 'loading' });

    void mediaApi.getContinueWatching(mediaTitleId)
      .then((value) => {
        if (!isCancelled) setState({ status: 'loaded', value });
      })
      .catch(() => {
        if (!isCancelled) setState({ status: 'error' });
      });

    return () => {
      isCancelled = true;
    };
  }, [mediaTitleId, isInLibrary, persistedProgress, episodeCatalog.status, episodeCatalogRevision]);

  return state;
}

type ReloadableResourceState<TValue, TError> =
  | { status: 'loading' }
  | { status: 'loaded'; value: TValue }
  | TError;

function useReloadableMediaResource<TValue, TError>(
  mediaTitleId: string | undefined,
  loader: (id: string) => Promise<TValue>,
  createErrorState: (error: unknown) => TError,
) {
  const [state, setState] = useState<ReloadableResourceState<TValue, TError>>({ status: 'loading' });

  const reload = useCallback(() => {
    if (!mediaTitleId) {
      setState({ status: 'loading' });
      return;
    }

    setState({ status: 'loading' });
    void loader(mediaTitleId)
      .then((value) => setState({ status: 'loaded', value }))
      .catch((error) => setState(createErrorState(error)));
  }, [createErrorState, loader, mediaTitleId]);

  useEffect(() => {
    reload();
  }, [reload]);

  return { state, reload };
}

const loadEpisodeCatalog = (mediaTitleId: string) => mediaApi.getEpisodes(mediaTitleId);
const createEpisodeCatalogError = () => ({ status: 'error' as const });
const loadFranchiseGraph = (mediaTitleId: string) => mediaApi.getFranchiseGraph(mediaTitleId);
const createFranchiseGraphError = (error: unknown) => ({
  status: 'error' as const,
  error: getErrorMessage(error, 'Failed to load franchise connections'),
});

export function useEpisodeCatalog(entry: MediaEntryDetailModel | null) {
  return useReloadableMediaResource(
    entry?.mediaTitleId,
    loadEpisodeCatalog,
    createEpisodeCatalogError,
  );
}

export function useFranchiseGraph(
  entry: MediaEntryDetailModel | null,
  franchiseMediaTitleId?: string,
) {
  return useReloadableMediaResource(
    entry ? franchiseMediaTitleId ?? entry.mediaTitleId : undefined,
    loadFranchiseGraph,
    createFranchiseGraphError,
  );
}

export function useRemoteEntryRefresh(
  entry: MediaEntryDetailModel | null,
  reloadEntry: () => Promise<MediaEntryDetailModel>,
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

export function useManualRemoteRefresh(
  entry: MediaEntryDetailModel | null,
  reloadEntry: () => Promise<MediaEntryDetailModel>,
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

function getStatusChanges(entry: MediaEntryDetailModel, draft: StatusDraft) {
  return {
    statusChanged: draft.selectedStatus !== entry.status,
    progressChanged:
      draft.progressEpisodes !== entry.progressEpisodes
      || draft.progressChapters !== entry.progressChapters
      || draft.progressVolumes !== entry.progressVolumes,
  };
}

async function saveStatusChanges(
  mediaTitleId: string,
  draft: StatusDraft,
  changes: ReturnType<typeof getStatusChanges>,
) {
  if (changes.statusChanged) {
    await mediaApi.updateStatus(mediaTitleId, { status: draft.selectedStatus });
  }

  if (changes.progressChanged) {
    await mediaApi.updateProgress(mediaTitleId, {
      progressEpisodes: draft.progressEpisodes,
      progressChapters: draft.progressChapters,
      progressVolumes: draft.progressVolumes,
    });
  }
}

function applySavedStatus(
  current: MediaEntryDetailModel | null,
  draft: StatusDraft,
) {
  return current
    ? {
      ...current,
      status: draft.selectedStatus,
      progressEpisodes: draft.progressEpisodes,
      progressChapters: draft.progressChapters,
      progressVolumes: draft.progressVolumes,
    }
    : current;
}

export function useStatusSaveAction(
  mediaTitleId: string,
  entry: MediaEntryDetailModel | null,
  selectedStatus: string,
  progressEpisodes: number | undefined,
  progressChapters: number | undefined,
  progressVolumes: number | undefined,
  setEntry: Dispatch<SetStateAction<MediaEntryDetailModel | null>>,
  showSnackbar: ShowSnackbar,
) {
  const [isSavingStatus, setIsSavingStatus] = useState(false);

  const handleSaveStatus = useCallback(async () => {
    if (!entry) return;

    const draft = { selectedStatus, progressEpisodes, progressChapters, progressVolumes };
    const changes = getStatusChanges(entry, draft);

    setIsSavingStatus(true);
    try {
      await saveStatusChanges(mediaTitleId, draft, changes);
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
    mediaTitleId,
    progressChapters,
    progressEpisodes,
    progressVolumes,
    selectedStatus,
    setEntry,
    showSnackbar,
  ]);

  return { isSavingStatus, handleSaveStatus };
}

function applySavedScore(
  current: MediaEntryDetailModel | null,
  score: number | null,
) {
  return current ? { ...current, score } : current;
}

async function reconcileScoreSaveFailure(
  reloadEntry: () => Promise<MediaEntryDetailModel>,
  score: number | null,
  previousScore: number | null,
  setEntry: Dispatch<SetStateAction<MediaEntryDetailModel | null>>,
  showSnackbar: ShowSnackbar,
  saveError: unknown,
) {
  try {
    const reconciledEntry = await reloadEntry();
    const providerSyncPending = reconciledEntry.isInLibrary && reconciledEntry.score === score;
    showSnackbar(providerSyncPending
      ? {
        message: 'Score saved in Cantaro; provider sync may still be pending.',
        variant: 'info',
      }
      : {
        message: 'Score sync failed; showing the latest saved value.',
        variant: 'error',
      });
  } catch {
    setEntry((current) => applySavedScore(current, previousScore));
    showSnackbar({
      message: getPrefixedErrorMessage(saveError, 'Could not confirm score sync; score reverted'),
      variant: 'error',
    });
  }
}

export function useScoreSaveAction(
  mediaTitleId: string,
  entry: MediaEntryDetailModel | null,
  setEntry: Dispatch<SetStateAction<MediaEntryDetailModel | null>>,
  reloadEntry: () => Promise<MediaEntryDetailModel>,
  showSnackbar: ShowSnackbar,
) {
  const [isSavingScore, setIsSavingScore] = useState(false);

  const handleScoreChange = useCallback(async (score: number | null) => {
    if (!entry || isSavingScore || score === entry.score) return;

    const previousScore = entry.score;
    setEntry((current) => applySavedScore(current, score));
    setIsSavingScore(true);

    try {
      await mediaApi.updateScore(mediaTitleId, { score });
      showSnackbar({ message: 'Score saved', variant: 'success' });
    } catch (saveError) {
      // The API persists the local viewer state before attempting provider sync.
      // Reload first so a provider error cannot erase a score Cantaro already saved.
      await reconcileScoreSaveFailure(
        reloadEntry,
        score,
        previousScore,
        setEntry,
        showSnackbar,
        saveError,
      );
    } finally {
      setIsSavingScore(false);
    }
  }, [entry, isSavingScore, mediaTitleId, reloadEntry, setEntry, showSnackbar]);

  return { isSavingScore, handleScoreChange };
}

export function useProviderUnlinkAction(
  mediaTitleId: string,
  setEntry: Dispatch<SetStateAction<MediaEntryDetailModel | null>>,
  showSnackbar: ShowSnackbar,
) {
  const [unlinkingId, setUnlinkingId] = useState<string | null>(null);

  const handleUnlink = useCallback(async (providerId: string) => {
    setUnlinkingId(providerId);

    try {
      await mediaApi.unlinkProvider(mediaTitleId, providerId);
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
  }, [mediaTitleId, setEntry, showSnackbar]);

  return { unlinkingId, handleUnlink };
}
