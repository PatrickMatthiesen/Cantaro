import { ChevronDown, SlidersHorizontal } from 'lucide-react';
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
    filters: MediaLibraryQueryParams;
    availableProviderListNames: string[];
    providerStatus: MediaProviderAccountStatusDto | null;
    isRefreshing: boolean;
    isPrimaryProviderSelected: boolean;
    onUpdateFilter: <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => void;
    onUpdateProviderFilter: (provider: string | undefined) => void;
    onToggleSortDir: () => void;
    onRefreshFromRemote: () => Promise<void>;
    onNavigateProviders?: () => void;
}

// fallow-ignore-next-line complexity
export function LibraryFiltersPanel({
    filters,
    availableProviderListNames,
    providerStatus,
    isRefreshing,
    isPrimaryProviderSelected,
    onUpdateFilter,
    onUpdateProviderFilter,
    onToggleSortDir,
    onRefreshFromRemote,
    onNavigateProviders,
}: LibraryFiltersPanelProps) {
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
            <details className="group lg:hidden">
                <summary className="flex min-h-12 cursor-pointer list-none items-center gap-3 text-sm font-semibold text-content marker:hidden focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus">
                    <SlidersHorizontal className="size-4 text-content-muted" aria-hidden />
                    <span>Filters</span>
                    <span className="ml-auto text-xs font-normal text-content-muted">
                        {activeFilterCount > 0 ? `${activeFilterCount} active` : 'All titles'}
                    </span>
                    <ChevronDown className="size-4 text-content-muted transition-transform group-open:rotate-180" aria-hidden />
                </summary>
                <div className="flex flex-col gap-4 border-t border-border-subtle py-4">
                    <div className="grid grid-cols-2 gap-3 max-sm:grid-cols-1">{filterFields}</div>
                    {refreshAction}
                </div>
            </details>

            <div className="hidden flex-wrap items-end gap-x-3 gap-y-4 py-4 lg:flex">
                {filterFields}
                {refreshAction}
            </div>
        </section>
    );
}
