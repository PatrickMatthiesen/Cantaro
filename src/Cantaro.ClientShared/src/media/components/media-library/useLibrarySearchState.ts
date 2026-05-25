import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { MutableRefObject } from 'react';
import { mediaApi, type MediaLibraryQueryParams, type MediaProviderSearchResultDto } from '../../services/mediaApi';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import type { LibrarySearchMode } from './LibrarySearchBar';

const SEARCH_DEBOUNCE_MS = 450;

type UpdateLibraryFilter = <K extends keyof MediaLibraryQueryParams>(
    key: K,
    value: MediaLibraryQueryParams[K],
) => void;

interface UseLibrarySearchStateOptions {
    filters: MediaLibraryQueryParams;
    providerId?: string;
    isProviderConnected: boolean;
    updateFilter: UpdateLibraryFilter;
}

interface CatalogSearchControls {
    setCatalogResults: (results: MediaProviderSearchResultDto[]) => void;
    setIsCatalogSearching: (isSearching: boolean) => void;
    setCatalogSearchError: (error: string | null) => void;
    setHasCatalogSearched: (hasSearched: boolean) => void;
}

async function loadProviderResults(providerId: string, query: string) {
    return mediaApi.searchProvider(providerId, {
        query,
        limit: 40,
    });
}

function providerDisplayName(mode: LibrarySearchMode): string {
    return mediaProviderCatalog.find((provider) => provider.id === mode)?.name ?? 'Provider';
}

function useLatestFilterUpdater(updateFilter: UpdateLibraryFilter) {
    const updateFilterRef = useRef(updateFilter);

    useEffect(() => {
        updateFilterRef.current = updateFilter;
    }, [updateFilter]);

    return updateFilterRef;
}

function beginCatalogSearch(requestRef: MutableRefObject<number>, controls: CatalogSearchControls) {
    const requestId = requestRef.current + 1;
    requestRef.current = requestId;
    controls.setIsCatalogSearching(true);
    controls.setCatalogSearchError(null);
    controls.setHasCatalogSearched(true);
    return requestId;
}

function isCurrentRequest(requestRef: MutableRefObject<number>, requestId: number) {
    return requestRef.current === requestId;
}

function applyCatalogResults(
    requestRef: MutableRefObject<number>,
    requestId: number,
    controls: CatalogSearchControls,
    results: MediaProviderSearchResultDto[],
) {
    if (isCurrentRequest(requestRef, requestId)) controls.setCatalogResults(results);
}

function applyCatalogError(
    requestRef: MutableRefObject<number>,
    requestId: number,
    controls: CatalogSearchControls,
    err: unknown,
) {
    if (!isCurrentRequest(requestRef, requestId)) return;
    controls.setCatalogResults([]);
    controls.setCatalogSearchError(err instanceof Error ? err.message : 'Failed to search provider');
}

function finishCatalogSearch(
    requestRef: MutableRefObject<number>,
    requestId: number,
    controls: CatalogSearchControls,
) {
    if (isCurrentRequest(requestRef, requestId)) controls.setIsCatalogSearching(false);
}

function resetCatalogSearch(requestRef: MutableRefObject<number>, controls: CatalogSearchControls) {
    requestRef.current += 1;
    controls.setCatalogResults([]);
    controls.setCatalogSearchError(null);
    controls.setHasCatalogSearched(false);
    controls.setIsCatalogSearching(false);
}

function useCatalogSearch() {
    const [catalogResults, setCatalogResults] = useState<MediaProviderSearchResultDto[]>([]);
    const [isCatalogSearching, setIsCatalogSearching] = useState(false);
    const [catalogSearchError, setCatalogSearchError] = useState<string | null>(null);
    const [hasCatalogSearched, setHasCatalogSearched] = useState(false);
    const searchRequestIdRef = useRef(0);
    const controls = useMemo(
        () => ({ setCatalogResults, setIsCatalogSearching, setCatalogSearchError, setHasCatalogSearched }),
        [],
    );

    const runCatalogSearch = useCallback(async (providerId: string, query: string) => {
        const trimmedQuery = query.trim();
        if (!trimmedQuery) return;

        const requestId = beginCatalogSearch(searchRequestIdRef, controls);
        try {
            applyCatalogResults(searchRequestIdRef, requestId, controls, await loadProviderResults(providerId, trimmedQuery));
        } catch (err) {
            applyCatalogError(searchRequestIdRef, requestId, controls, err);
        } finally {
            finishCatalogSearch(searchRequestIdRef, requestId, controls);
        }
    }, [controls]);
    const resetSearch = useCallback(() => {
        resetCatalogSearch(searchRequestIdRef, controls);
    }, [controls]);

    return {
        catalogResults,
        isCatalogSearching,
        catalogSearchError,
        hasCatalogSearched,
        runCatalogSearch,
        resetCatalogSearch: resetSearch,
    };
}

function useDebouncedSearch(
    searchMode: LibrarySearchMode,
    searchQuery: string,
    updateFilterRef: MutableRefObject<UpdateLibraryFilter>,
    runCatalogSearch: (providerId: string, query: string) => Promise<void>,
    resetCatalogSearch: () => void,
) {
    useEffect(() => {
        const timeoutId = setTimeout(() => {
            const trimmedQuery = searchQuery.trim();
            if (searchMode === 'library') {
                updateFilterRef.current('query', trimmedQuery || undefined);
            } else if (trimmedQuery) {
                void runCatalogSearch(searchMode, trimmedQuery);
            } else {
                resetCatalogSearch();
            }
        }, SEARCH_DEBOUNCE_MS);

        return () => clearTimeout(timeoutId);
    }, [resetCatalogSearch, runCatalogSearch, searchMode, searchQuery, updateFilterRef]);
}

export function useLibrarySearchState({
    filters,
    providerId,
    isProviderConnected,
    updateFilter,
}: UseLibrarySearchStateOptions) {
    const [searchMode, setSearchMode] = useState<LibrarySearchMode>('library');
    const [searchQuery, setSearchQuery] = useState(() => filters.query ?? '');
    const updateFilterRef = useLatestFilterUpdater(updateFilter);
    const catalogSearch = useCatalogSearch();

    const connectedProviderIds = useMemo(
        () => (isProviderConnected && providerId ? [providerId] : []),
        [isProviderConnected, providerId],
    );

    useDebouncedSearch(searchMode, searchQuery, updateFilterRef, catalogSearch.runCatalogSearch, catalogSearch.resetCatalogSearch);

    const handleSearchSubmit = useCallback(() => {
        const trimmedQuery = searchQuery.trim();
        if (searchMode === 'library') {
            updateFilter('query', trimmedQuery || undefined);
        } else if (trimmedQuery) {
            void catalogSearch.runCatalogSearch(searchMode, trimmedQuery);
        }
    }, [catalogSearch, searchMode, searchQuery, updateFilter]);

    const handleSearchModeChange = useCallback((mode: LibrarySearchMode) => {
        setSearchMode(mode);
        catalogSearch.resetCatalogSearch();
    }, [catalogSearch]);

    return {
        searchMode,
        searchQuery,
        catalogResults: catalogSearch.catalogResults,
        isCatalogSearching: catalogSearch.isCatalogSearching,
        catalogSearchError: catalogSearch.catalogSearchError,
        hasCatalogSearched: catalogSearch.hasCatalogSearched,
        connectedProviderIds,
        providerName: providerDisplayName(searchMode),
        setSearchQuery,
        handleSearchSubmit,
        handleSearchModeChange,
        runCatalogSearch: catalogSearch.runCatalogSearch,
    };
}
