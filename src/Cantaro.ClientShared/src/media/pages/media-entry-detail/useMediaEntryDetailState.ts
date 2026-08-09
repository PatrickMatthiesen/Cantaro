import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { providerAvailabilityKey, type ProviderAvailabilityMap } from '../../components/media-entry-detail/providerAvailability';
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
  MediaLibraryEntryDetailDto,
  MediaLibraryImportEventDto,
  MediaProviderLinkSummaryDto,
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

export function useEntryDetailState(libraryEntryId: string) {
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

export function useProviderAvailability(entry: MediaLibraryEntryDetailDto | null) {
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
    .map((episode) => `${episode.episodeNumber}:${episode.destinations
      .map((destination) => `${destination.serviceId}:${destination.url}`)
      .join(',')}:${episode.hasConflict}`)
    .join('|');
}

export function useContinueWatching(
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

export function useEpisodeCatalog(entry: MediaLibraryEntryDetailDto | null) {
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

export function useRemoteEntryRefresh(
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

export function useManualRemoteRefresh(
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

export function useStatusSaveAction(
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

export function useProviderUnlinkAction(
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
