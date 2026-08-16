import { useState } from 'react';
import { ChevronDown, Search, SlidersHorizontal } from 'lucide-react';
import {
    LibraryFilterFields,
    LibraryRefreshAction,
} from './LibraryFilterControls';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import type { MediaLibraryQueryParams, MediaProviderAccountStatusDto } from '../../services/mediaApi';

const STATUS_OPTIONS = [
    { value: '', label: 'All statuses' },
    { value: 'current', label: 'Watching / Reading' },
    { value: 'completed', label: 'Completed' },
    { value: 'planned', label: 'Planning' },
    { value: 'paused', label: 'Paused' },
    { value: 'dropped', label: 'Dropped' },
    { value: 'repeating', label: 'Rewatching / Rereading' },
];

const MEDIA_KIND_OPTIONS = [
    { value: '', label: 'All types' },
    { value: 'anime', label: 'Anime' },
    { value: 'manga', label: 'Manga' },
    { value: 'lightNovel', label: 'Light Novel' },
    { value: 'oneShot', label: 'One Shot' },
];

interface FilterOption {
    value: string;
    label: string;
}

export interface LibraryFiltersPanelProps {
    searchQuery: string;
    filters: MediaLibraryQueryParams;
    availableProviderListNames: string[];
    providerStatus: MediaProviderAccountStatusDto | null;
    isRefreshing: boolean;
    isPrimaryProviderSelected: boolean;
    onSearchQueryChange: (query: string) => void;
    onUpdateFilter: <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => void;
    onUpdateProviderFilter: (provider: string | undefined) => void;
    onToggleSortDir: () => void;
    onRefreshFromRemote: () => Promise<void>;
    onNavigateProviders?: () => void;
}

// fallow-ignore-next-line complexity
export function LibraryFiltersPanel({
    searchQuery,
    filters,
    availableProviderListNames,
    providerStatus,
    isRefreshing,
    isPrimaryProviderSelected,
    onSearchQueryChange,
    onUpdateFilter,
    onUpdateProviderFilter,
    onToggleSortDir,
    onRefreshFromRemote,
    onNavigateProviders,
}: LibraryFiltersPanelProps) {
    const [filtersOpen, setFiltersOpen] = useState(false);
    const providerOptions: FilterOption[] = [
        { value: '', label: 'All providers' },
        ...mediaProviderCatalog.map((provider) => ({ value: provider.id, label: provider.name })),
    ];
    const listOptions: FilterOption[] = [
        { value: '', label: 'All lists' },
        ...availableProviderListNames.map((listName) => ({ value: listName, label: listName })),
    ];
    const activeFilterCount = [
        filters.status,
        filters.mediaKind,
        filters.provider,
        filters.providerListName,
    ].filter(Boolean).length;
    const filterFields = (
        <LibraryFilterFields
            statusValue={filters.status ?? ''}
            mediaKindValue={filters.mediaKind ?? ''}
            providerValue={filters.provider ?? ''}
            providerListNameValue={filters.providerListName ?? ''}
            providerOptions={providerOptions}
            listOptions={listOptions}
            statusOptions={STATUS_OPTIONS}
            mediaKindOptions={MEDIA_KIND_OPTIONS}
            isListDisabled={availableProviderListNames.length === 0 || !isPrimaryProviderSelected}
            sortBy={filters.sortBy ?? 'updatedAt'}
            sortDir={filters.sortDir ?? 'desc'}
            onStatusChange={(value) => onUpdateFilter('status', value)}
            onMediaKindChange={(value) => onUpdateFilter('mediaKind', value)}
            onProviderChange={onUpdateProviderFilter}
            onProviderListChange={(value) => onUpdateFilter('providerListName', value)}
            onSortByChange={(value) => onUpdateFilter('sortBy', value)}
            onToggleSortDir={onToggleSortDir}
        />
    );
    const refreshAction = (
        <LibraryRefreshAction
            isConnected={Boolean(providerStatus?.isConnected)}
            isRefreshing={isRefreshing}
            onRefresh={onRefreshFromRemote}
            onNavigateProviders={onNavigateProviders}
        />
    );

    return (
        <section className="border-y border-border-subtle">
            <div className="flex items-center gap-2 py-3">
                <label className="relative min-w-0 flex-1">
                    <span className="sr-only">Search library</span>
                    <Search className="pointer-events-none absolute top-1/2 left-3 size-5 -translate-y-1/2 text-content-subtle" aria-hidden />
                    <input
                        type="search"
                        value={searchQuery}
                        onChange={(event) => onSearchQueryChange(event.target.value)}
                        placeholder="Search library…"
                        className="h-11 w-full border border-border-strong bg-surface pr-4 pl-10 text-sm text-content outline-none transition-colors placeholder:text-content-subtle hover:bg-surface-hover focus:border-focus focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
                    />
                </label>
                <button
                    type="button"
                    aria-expanded={filtersOpen}
                    aria-controls="library-filter-controls"
                    className="inline-flex min-h-11 shrink-0 items-center gap-2 px-3 text-sm font-semibold text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus lg:hidden"
                    onClick={() => setFiltersOpen((open) => !open)}
                >
                    <SlidersHorizontal className="size-4" aria-hidden />
                    <span>Filters</span>
                    {activeFilterCount > 0 ? <span className="text-xs font-normal">{activeFilterCount}</span> : null}
                    <ChevronDown className={`size-4 transition-transform ${filtersOpen ? 'rotate-180' : ''}`} aria-hidden />
                </button>
            </div>

            <div
                id="library-filter-controls"
                className={`${filtersOpen ? 'flex' : 'hidden'} flex-col gap-4 border-t border-border-subtle py-4 lg:flex lg:flex-row lg:flex-wrap lg:items-end lg:gap-x-3 lg:gap-y-4`}
            >
                <div className="grid grid-cols-2 gap-3 max-sm:grid-cols-1 lg:contents">{filterFields}</div>
                {refreshAction}
            </div>
        </section>
    );
}
