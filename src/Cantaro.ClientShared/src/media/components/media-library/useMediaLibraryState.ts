import { useCallback, useEffect, useRef, useState } from 'react';
import type { MutableRefObject } from 'react';
import { mediaApi } from '../../services/mediaApi';
import { mainMediaProviderId } from '../../services/mediaProviders';
import {
  formatTimestamp,
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
  clearStoredValue,
} from '../../services/mediaRefreshCache';
import type {
  MediaLibraryListItemDto,
  MediaLibraryQueryParams,
  MediaProviderAccountStatusDto,
} from '../../services/mediaApi';

const PRIMARY_PROVIDER_ID = mainMediaProviderId;
const DEFAULT_PRIMARY_LIST_NAME = 'Watching';

function storedListNameKey(providerId: string): string {
  return `cantaro.media.provider.${providerId}.lastListName`;
}

function listNameForProvider(provider: string | undefined): string | undefined {
  if (provider !== PRIMARY_PROVIDER_ID) {
    return undefined;
  }

  return readStoredValue(storedListNameKey(PRIMARY_PROVIDER_ID)) || undefined;
}

function hasActiveMediaLibraryFilters(filters: MediaLibraryQueryParams): boolean {
  return Boolean(
    filters.status
    || filters.query
    || filters.mediaKind
    || filters.listName
    || (filters.provider && filters.provider !== PRIMARY_PROVIDER_ID)
  );
}

function shouldRefreshPrimaryProvider(status: MediaProviderAccountStatusDto): boolean {
  return status.isConnected && isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(PRIMARY_PROVIDER_ID)));
}

function useLibraryDataState() {
  const [items, setItems] = useState<MediaLibraryListItemDto[]>([]);
  const [availableListNames, setAvailableListNames] = useState<string[]>([]);
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

  return {
    items,
    availableListNames,
    totalPages,
    totalCount,
    isLoading,
    error,
    loadLibrary,
  };
}

function useLibraryFilters(availableListNames: string[]) {
  const initialStoredListNameRef = useRef(readStoredValue(storedListNameKey(PRIMARY_PROVIDER_ID)));
  const hasAppliedInitialListFallbackRef = useRef(Boolean(initialStoredListNameRef.current));
  const [filters, setFilters] = useState<MediaLibraryQueryParams>(() => ({
    provider: PRIMARY_PROVIDER_ID,
    listName: initialStoredListNameRef.current || undefined,
    sortBy: 'updatedAt',
    sortDir: 'desc',
    page: 1,
    pageSize: 24,
  }));

  useEffect(() => {
    if (filters.provider !== PRIMARY_PROVIDER_ID) {
      return;
    }

    const storageKey = storedListNameKey(PRIMARY_PROVIDER_ID);
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

  useEffect(() => {
    if (hasAppliedInitialListFallbackRef.current || filters.provider !== PRIMARY_PROVIDER_ID || availableListNames.length === 0) {
      return;
    }

    hasAppliedInitialListFallbackRef.current = true;
    if (filters.listName || !availableListNames.includes(DEFAULT_PRIMARY_LIST_NAME)) {
      return;
    }

    setFilters((prev) => ({ ...prev, listName: DEFAULT_PRIMARY_LIST_NAME, page: 1 }));
  }, [availableListNames, filters.listName, filters.provider]);

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
      listName: listNameForProvider(provider),
      page: 1,
    }));
  };

  const goToPreviousPage = () => {
    setFilters((prev) => ({ ...prev, page: Math.max(1, (prev.page ?? 1) - 1) }));
  };

  const goToNextPage = () => {
    setFilters((prev) => ({ ...prev, page: (prev.page ?? 1) + 1 }));
  };

  return {
    filters,
    setFilters,
    updateFilter,
    toggleSortDir,
    updateProviderFilter,
    goToPreviousPage,
    goToNextPage,
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

  const refreshFromRemote = useCallback(async () => {
    setIsRefreshing(true);
    setRefreshError(null);

    try {
      const result = await mediaApi.importLibrary(PRIMARY_PROVIDER_ID);
      writeStoredValue(remoteCheckTimestampKey(PRIMARY_PROVIDER_ID), result.importedAt);
      setLastRemoteCheckAt(result.importedAt);
      await loadLibrary(filtersRef.current);
    } catch (err) {
      setRefreshError(err instanceof Error ? err.message : 'Failed to refresh from AniList');
    } finally {
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

  return {
    providerStatus,
    isRefreshing,
    refreshError,
    refreshFromRemote,
    formattedLastRemoteCheckAt: formatTimestamp(lastRemoteCheckAt),
  };
}

export function useMediaLibraryState() {
  const libraryData = useLibraryDataState();
  const { availableListNames, loadLibrary } = libraryData;
  const filterState = useLibraryFilters(availableListNames);
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
