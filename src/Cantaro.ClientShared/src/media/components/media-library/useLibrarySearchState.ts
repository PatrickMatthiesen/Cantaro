import { useCallback, useEffect, useRef, useState } from 'react';
import type { MediaLibraryQueryParams } from '../../services/mediaApi';

const SEARCH_DEBOUNCE_MS = 450;

type UpdateLibraryFilter = <K extends keyof MediaLibraryQueryParams>(
    key: K,
    value: MediaLibraryQueryParams[K],
) => void;

interface UseLibrarySearchStateOptions {
    filters: MediaLibraryQueryParams;
    updateFilter: UpdateLibraryFilter;
    searchQuery?: string;
    onSearchQueryChange?: (query: string) => void;
}

export function useLibrarySearchState({
    filters,
    updateFilter,
    searchQuery: controlledSearchQuery,
    onSearchQueryChange,
}: UseLibrarySearchStateOptions) {
    const [internalSearchQuery, setInternalSearchQuery] = useState(filters.query ?? '');
    const searchQuery = controlledSearchQuery ?? internalSearchQuery;
    const updateFilterRef = useRef(updateFilter);

    useEffect(() => {
        updateFilterRef.current = updateFilter;
    }, [updateFilter]);

    useEffect(() => {
        const timeoutId = setTimeout(() => {
            const trimmedQuery = searchQuery.trim();
            updateFilterRef.current('query', trimmedQuery || undefined);
        }, SEARCH_DEBOUNCE_MS);

        return () => clearTimeout(timeoutId);
    }, [searchQuery]);

    const setSearchQuery = useCallback((query: string) => {
        if (onSearchQueryChange) {
            onSearchQueryChange(query);
            return;
        }

        setInternalSearchQuery(query);
    }, [onSearchQueryChange]);

    return { searchQuery, setSearchQuery };
}
