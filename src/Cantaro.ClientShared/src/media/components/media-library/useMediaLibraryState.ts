import { useCallback, useEffect, useRef, useState } from 'react';
import type { Dispatch, MutableRefObject, SetStateAction } from 'react';
import { mediaApi } from '../../services/mediaApi';
import { mainMediaProviderId } from '../../services/mediaProviders';
import {
  formatTimestamp,
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../../services/mediaRefreshCache';
import type {
  MediaLibraryListItemDto,
  MediaLibraryImportEventDto,
  MediaLibraryQueryParams,
  MediaProviderAccountStatusDto,
} from '../../services/mediaApi';
import {
  createInitialMediaLibraryFilters,
  definedMediaLibraryFilterDefaults,
} from './mediaLibraryFilters';

const PRIMARY_PROVIDER_ID = mainMediaProviderId;
const ANILIST_REFRESH_ERROR = 'AniList couldn’t be refreshed. Cantaro will keep using your saved library data; use Reload to try again.';
export type MediaLibraryFilterDefaults = Partial<MediaLibraryQueryParams>;

function serializeFilterDefaults(filterDefaults?: MediaLibraryFilterDefaults) {
  return JSON.stringify(filterDefaults ?? {});
}

function hasActiveMediaLibraryFilters(filters: MediaLibraryQueryParams): boolean {
  return Boolean(
    filters.status
    || filters.query
    || filters.mediaKind
    || filters.providerListName
    || (filters.provider && filters.provider !== PRIMARY_PROVIDER_ID)
  );
}

function shouldRefreshPrimaryProvider(status: MediaProviderAccountStatusDto): boolean {
  return status.isConnected && isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(PRIMARY_PROVIDER_ID)));
}

function useLibraryDataState() {
  const [items, setItems] = useState<MediaLibraryListItemDto[]>([]);
  const [availableProviderListNames, setAvailableProviderListNames] = useState<string[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadLibrary = useCallback(async (params: MediaLibraryQueryParams) => {
    setIsLoading(true);
    setError(null);

    try {
      const result = await mediaApi.getLibrary(params);
      setItems(result.items);
      setAvailableProviderListNames(result.availableProviderListNames);
      setTotalPages(result.totalPages);
      setTotalCount(result.totalCount);
    } catch (err) {
      setAvailableProviderListNames([]);
      setError(err instanceof Error ? err.message : 'Failed to load library');
    } finally {
      setIsLoading(false);
    }
  }, []);

  return {
    items,
    availableProviderListNames,
    totalPages,
    totalCount,
    isLoading,
    error,
    loadLibrary,
  };
}

function useFilterDefaults(
  setFilters: Dispatch<SetStateAction<MediaLibraryQueryParams>>,
  filterDefaults?: MediaLibraryFilterDefaults,
) {
  const filterDefaultsKey = serializeFilterDefaults(filterDefaults);

  useEffect(() => {
    if (!filterDefaults) {
      return;
    }

    setFilters((prev) => ({
      ...prev,
      ...definedMediaLibraryFilterDefaults(filterDefaults),
      page: 1,
      pageSize: prev.pageSize ?? 24,
    }));
  }, [filterDefaults, filterDefaultsKey]);
}

function useAvailableProviderListNameGuard(
  availableProviderListNames: string[],
  filters: MediaLibraryQueryParams,
  setFilters: Dispatch<SetStateAction<MediaLibraryQueryParams>>,
) {
  useEffect(() => {
    if (!filters.providerListName || availableProviderListNames.length === 0 || availableProviderListNames.includes(filters.providerListName)) {
      return;
    }

    setFilters((prev) => ({ ...prev, providerListName: undefined, page: 1 }));
  }, [availableProviderListNames, filters.providerListName]);
}

function useLibraryFilterActions(setFilters: Dispatch<SetStateAction<MediaLibraryQueryParams>>) {
  const updateFilter = <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => {
    setFilters((prev) => ({ ...prev, [key]: value, page: 1 }));
  };

  const toggleSortDir = () => {
    setFilters((prev) => ({
      ...prev,
      sortDir: prev.sortDir === 'asc' ? 'desc' : 'asc',
      page: 1,
    }));
  };

  const updateProviderFilter = (provider: string | undefined) => {
    setFilters((prev) => ({
      ...prev,
      provider,
      providerListName: undefined,
      page: 1,
    }));
  };

  const goToPreviousPage = () => {
    setFilters((prev) => ({ ...prev, page: Math.max(1, (prev.page ?? 1) - 1) }));
  };

  const goToNextPage = () => {
    setFilters((prev) => ({ ...prev, page: (prev.page ?? 1) + 1 }));
  };

  return { updateFilter, toggleSortDir, updateProviderFilter, goToPreviousPage, goToNextPage };
}

function useLibraryFilters(availableProviderListNames: string[], filterDefaults?: MediaLibraryFilterDefaults) {
  const [filters, setFilters] = useState<MediaLibraryQueryParams>(() => createInitialMediaLibraryFilters(filterDefaults));
  const actions = useLibraryFilterActions(setFilters);

  useFilterDefaults(setFilters, filterDefaults);
  useAvailableProviderListNameGuard(availableProviderListNames, filters, setFilters);

  return {
    filters,
    setFilters,
    ...actions,
    hasActiveFilters: hasActiveMediaLibraryFilters(filters),
    isPrimaryProviderSelected: filters.provider === PRIMARY_PROVIDER_ID,
  };
}

function useProviderRefresh(
  loadLibrary: (params: MediaLibraryQueryParams) => Promise<void>,
  filtersRef: MutableRefObject<MediaLibraryQueryParams>,
) {
  const [providerStatus, setProviderStatus] = useState<MediaProviderAccountStatusDto | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [lastRemoteCheckAt, setLastRemoteCheckAt] = useState<string | null>(() => readStoredValue(remoteCheckTimestampKey(PRIMARY_PROVIDER_ID)));
  const activeImportEventsRef = useRef<EventSource | null>(null);

  useEffect(() => () => {
    activeImportEventsRef.current?.close();
  }, []);

  const refreshFromRemote = useCallback(async () => {
    activeImportEventsRef.current?.close();
    activeImportEventsRef.current = null;
    setIsRefreshing(true);
    setRefreshError(null);

    try {
      const request = await mediaApi.importLibrary(PRIMARY_PROVIDER_ID);
      const eventSource = await mediaApi.subscribeToImportEvents(
        PRIMARY_PROVIDER_ID,
        request.importId,
        (event: MediaLibraryImportEventDto) => {
          if (event.status === 'queued' || event.status === 'running') {
            setIsRefreshing(true);
            return;
          }

          activeImportEventsRef.current?.close();
          activeImportEventsRef.current = null;

          if (event.status === 'completed') {
            writeStoredValue(remoteCheckTimestampKey(PRIMARY_PROVIDER_ID), event.occurredAt);
            setLastRemoteCheckAt(event.occurredAt);
            setIsRefreshing(false);
            void loadLibrary(filtersRef.current);
            return;
          }

          if (event.status === 'failed') {
            setRefreshError(ANILIST_REFRESH_ERROR);
            setIsRefreshing(false);
          }
        },
        () => {
          activeImportEventsRef.current?.close();
          activeImportEventsRef.current = null;
          setRefreshError(ANILIST_REFRESH_ERROR);
          setIsRefreshing(false);
        },
      );
      activeImportEventsRef.current = eventSource;
    } catch {
      setRefreshError(ANILIST_REFRESH_ERROR);
      setIsRefreshing(false);
    }
  }, [filtersRef, loadLibrary]);

  useEffect(() => {
    let isCancelled = false;

    const loadProviderState = async () => {
      try {
        const status = await mediaApi.getProviderStatus(PRIMARY_PROVIDER_ID);
        if (isCancelled) {
          return;
        }

        setProviderStatus(status);
        if (shouldRefreshPrimaryProvider(status)) {
          await refreshFromRemote();
        }
      } catch {
        if (!isCancelled) {
          setRefreshError(ANILIST_REFRESH_ERROR);
        }
      }
    };

    void loadProviderState();

    return () => {
      isCancelled = true;
    };
  }, [refreshFromRemote]);

  return {
    providerStatus,
    isRefreshing,
    refreshError,
    refreshFromRemote,
    formattedLastRemoteCheckAt: formatTimestamp(lastRemoteCheckAt),
  };
}

export function useMediaLibraryState(filterDefaults?: MediaLibraryFilterDefaults) {
  const libraryData = useLibraryDataState();
  const { availableProviderListNames, loadLibrary } = libraryData;
  const filterState = useLibraryFilters(availableProviderListNames, filterDefaults);
  const filtersRef = useRef(filterState.filters);

  useEffect(() => {
    filtersRef.current = filterState.filters;
  }, [filterState.filters]);

  useEffect(() => {
    void loadLibrary(filterState.filters);
  }, [filterState.filters, loadLibrary]);

  const providerState = useProviderRefresh(loadLibrary, filtersRef);

  return {
    ...libraryData,
    ...filterState,
    ...providerState,
    primaryProviderId: PRIMARY_PROVIDER_ID,
  };
}
