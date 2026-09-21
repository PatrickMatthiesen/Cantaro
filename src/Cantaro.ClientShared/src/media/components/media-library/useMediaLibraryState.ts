import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { Dispatch, MutableRefObject, SetStateAction } from 'react';
import { mediaApi } from '../../services/mediaApi';
import { toMediaApiError, type MediaApiError } from '../../services/mediaApi.errors';
import { connectedMediaProviderIds } from '../../services/mediaProviders';
import { subscribeToMediaProgressUpdates } from '../../services/mediaProgressEvents';
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
import { libraryCollectionStorageKey } from './libraryCollections';

const PROVIDER_REFRESH_ERROR = 'Provider sync could not be refreshed. You can still use your saved Cantaro library.';
export type MediaLibraryFilterDefaults = Partial<MediaLibraryQueryParams>;

function serializeFilterDefaults(filterDefaults?: MediaLibraryFilterDefaults) {
  return JSON.stringify(filterDefaults ?? {});
}

function serializeLibraryQuery(params: MediaLibraryQueryParams): string {
  return JSON.stringify(
    Object.entries(params)
      .filter(([, value]) => value !== undefined)
      .sort(([left], [right]) => left.localeCompare(right)),
  );
}

function hasActiveMediaLibraryFilters(filters: MediaLibraryQueryParams): boolean {
  return Boolean(
    filters.status
    || filters.format
    || filters.query
    || filters.mediaKind
    || filters.providerListName
    || filters.provider
  );
}

function useLibraryDataState() {
  const [items, setItems] = useState<MediaLibraryListItemDto[]>([]);
  const [availableProviderListNames, setAvailableProviderListNames] = useState<string[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<MediaApiError | null>(null);
  const [loadedQueryKey, setLoadedQueryKey] = useState<string | null>(null);
  const activeQueryKeyRef = useRef<string | null>(null);
  const inFlightRequestsRef = useRef<Map<string, Promise<void>>>(new Map());

  const loadLibrary = useCallback(async (params: MediaLibraryQueryParams) => {
    const queryKey = serializeLibraryQuery(params);
    const inFlightRequest = inFlightRequestsRef.current.get(queryKey);
    if (inFlightRequest) {
      activeQueryKeyRef.current = queryKey;
      return inFlightRequest;
    }

    activeQueryKeyRef.current = queryKey;
    const request = (async () => {
      setIsLoading(true);
      setError(null);

      try {
        const result = await mediaApi.getLibrary(params);
        if (activeQueryKeyRef.current !== queryKey) return;
        setItems(result.items);
        setAvailableProviderListNames(result.availableProviderListNames);
        setTotalPages(result.totalPages);
        setTotalCount(result.totalCount);
        setLoadedQueryKey(queryKey);
      } catch (err) {
        if (activeQueryKeyRef.current !== queryKey) return;
        // Keep successful data and provider-list options available while a
        // background reload fails. The view decides whether that data matches
        // the active query before displaying it.
        setError(toMediaApiError(err, 'Cantaro could not load your library.'));
      } finally {
        if (activeQueryKeyRef.current === queryKey) {
          setIsLoading(false);
        }
      }
    })();
    inFlightRequestsRef.current.set(queryKey, request);
    void request.finally(() => {
      if (inFlightRequestsRef.current.get(queryKey) === request) {
        inFlightRequestsRef.current.delete(queryKey);
      }
    });
    return request;
  }, []);

  return {
    items,
    availableProviderListNames,
    totalPages,
    totalCount,
    isLoading,
    error,
    loadedQueryKey,
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
  }, [filterDefaults, filterDefaultsKey, setFilters]);
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
    setFilters((prev) => (prev[key] || undefined) === (value || undefined) ? prev : { ...prev, [key]: value, page: 1 });
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

  const updateCollection = (collection: string) => {
    writeStoredValue(libraryCollectionStorageKey, collection);
    setFilters((prev) => ({ ...prev, collection, mediaKind: undefined, format: undefined, page: 1 }));
  };

  const clearAdvancedFilters = () => {
    setFilters((prev) => ({ ...prev, status: '', format: undefined, provider: undefined, providerListName: undefined, page: 1 }));
  };

  const goToPreviousPage = () => {
    setFilters((prev) => ({ ...prev, page: Math.max(1, (prev.page ?? 1) - 1) }));
  };

  const goToNextPage = () => {
    setFilters((prev) => ({ ...prev, page: (prev.page ?? 1) + 1 }));
  };

  return { updateFilter, toggleSortDir, updateProviderFilter, updateCollection, clearAdvancedFilters, goToPreviousPage, goToNextPage };
}

function useLibraryFilters(availableProviderListNames: string[], filterDefaults?: MediaLibraryFilterDefaults,
  onFiltersChange?: (filters: MediaLibraryQueryParams) => void) {
  const filterDefaultsKey = serializeFilterDefaults(filterDefaults);
  const stableFilterDefaults = useMemo(() => filterDefaults, [filterDefaultsKey]);
  const [internalFilters, setInternalFilters] = useState<MediaLibraryQueryParams>(() => createInitialMediaLibraryFilters(filterDefaults));
  const routeFilters = useMemo(() => createInitialMediaLibraryFilters(stableFilterDefaults), [stableFilterDefaults]);
  const filters = onFiltersChange ? routeFilters : internalFilters;
  const setFilters: Dispatch<SetStateAction<MediaLibraryQueryParams>> = (update) => {
    if (!onFiltersChange) { setInternalFilters(update); return; }
    const next = typeof update === 'function' ? update(filters) : update;
    if (next !== filters) onFiltersChange(next);
  };
  const actions = useLibraryFilterActions(setFilters);

  useFilterDefaults(setInternalFilters, onFiltersChange ? undefined : stableFilterDefaults);
  useAvailableProviderListNameGuard(availableProviderListNames, filters, setFilters);

  return {
    filters,
    setFilters,
    ...actions,
    hasActiveFilters: hasActiveMediaLibraryFilters(filters),
  };
}

function useProviderRefresh(
  loadLibrary: (params: MediaLibraryQueryParams) => Promise<void>,
  filtersRef: MutableRefObject<MediaLibraryQueryParams>,
) {
  const [providerStatus, setProviderStatus] = useState<MediaProviderAccountStatusDto | null>(null);
  const connectedProviderIdsRef = useRef<string[]>([]);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [lastRemoteCheckAt, setLastRemoteCheckAt] = useState<string | null>(null);
  const activeImportEventsRef = useRef<Map<string, EventSource>>(new Map());

  const closeImportEvent = useCallback((providerId: string) => {
    activeImportEventsRef.current.get(providerId)?.close();
    activeImportEventsRef.current.delete(providerId);
  }, []);

  useEffect(() => () => {
    for (const eventSource of activeImportEventsRef.current.values()) {
      eventSource.close();
    }
    activeImportEventsRef.current.clear();
  }, []);

  const importProvider = useCallback((providerId: string) => new Promise<string>((resolve, reject) => {
    let settled = false;
    const settle = (callback: () => void) => {
      if (settled) return;
      settled = true;
      closeImportEvent(providerId);
      callback();
    };

    void mediaApi.importLibrary(providerId)
      .then(async (request) => {
        const eventSource = await mediaApi.subscribeToImportEvents(
          providerId,
          request.importId,
          (event: MediaLibraryImportEventDto) => {
            if (event.status === 'completed') {
              writeStoredValue(remoteCheckTimestampKey(providerId), event.occurredAt);
              setLastRemoteCheckAt(event.occurredAt);
              settle(() => resolve(event.occurredAt));
            } else if (event.status === 'failed') {
              settle(() => reject(new Error(event.errorMessage || PROVIDER_REFRESH_ERROR)));
            }
          },
          () => settle(() => reject(new Error(PROVIDER_REFRESH_ERROR))),
        );
        if (settled) {
          eventSource.close();
          return;
        }
        activeImportEventsRef.current.set(providerId, eventSource);
      })
      .catch((error) => settle(() => reject(error)));
  }), [closeImportEvent]);

  const refreshFromRemote = useCallback(async (providerIds?: string[]) => {
    const providersToRefresh = providerIds ?? connectedProviderIdsRef.current;
    if (providersToRefresh.length === 0) return;

    for (const providerId of providersToRefresh) {
      closeImportEvent(providerId);
    }
    setIsRefreshing(true);
    setRefreshError(null);

    const results = await Promise.allSettled(providersToRefresh.map((providerId) => importProvider(providerId)));
    if (results.some((result) => result.status === 'rejected')) {
      setRefreshError(PROVIDER_REFRESH_ERROR);
    }
    if (results.some((result) => result.status === 'fulfilled')) {
      void loadLibrary(filtersRef.current);
    }
    setIsRefreshing(false);
  }, [closeImportEvent, filtersRef, importProvider, loadLibrary]);

  useEffect(() => {
    let isCancelled = false;

    // fallow-ignore-next-line complexity
    const loadProviderState = async () => {
      try {
        const summary = await mediaApi.getProviderStatuses();
        if (isCancelled) {
          return;
        }

        const connectedIds = connectedMediaProviderIds(summary.statuses);
        connectedProviderIdsRef.current = connectedIds;
        const primaryProviderId = connectedIds[0];
        setProviderStatus(primaryProviderId !== undefined
          ? { providerId: primaryProviderId, isConnected: true }
          : null);
        if (summary.statuses.length === 0 && summary.failedProviderIds.length > 0) {
          setRefreshError(PROVIDER_REFRESH_ERROR);
          return;
        }

        const staleProviderIds = connectedIds.filter((providerId) =>
          isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(providerId))));
        if (staleProviderIds.length > 0) {
          await refreshFromRemote(staleProviderIds);
        }
      } catch {
        if (!isCancelled) {
          setRefreshError(PROVIDER_REFRESH_ERROR);
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

export function useMediaLibraryState(filterDefaults?: MediaLibraryFilterDefaults, onFiltersChange?: (filters: MediaLibraryQueryParams) => void) {
  const libraryData = useLibraryDataState();
  const { availableProviderListNames, loadLibrary } = libraryData;
  const filterState = useLibraryFilters(availableProviderListNames, filterDefaults, onFiltersChange);
  const filtersRef = useRef(filterState.filters);
  const currentQueryKey = serializeLibraryQuery(filterState.filters);

  useEffect(() => {
    filtersRef.current = filterState.filters;
  }, [filterState.filters]);

  useEffect(() => {
    void loadLibrary(filterState.filters);
  }, [filterState.filters, loadLibrary]);

  useEffect(() => subscribeToMediaProgressUpdates(() => {
    void loadLibrary(filtersRef.current);
  }), [loadLibrary]);

  const providerState = useProviderRefresh(loadLibrary, filtersRef);

  return {
    ...libraryData,
    hasCurrentData: libraryData.loadedQueryKey === currentQueryKey,
    ...filterState,
    ...providerState,
  };
}
