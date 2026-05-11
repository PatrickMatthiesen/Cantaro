import { GlassCard } from '../../../ui';
import {
    LibraryFilterFields,
    LibraryRefreshAction,
} from './LibraryFilterControls';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import type { MediaLibraryQueryParams, MediaProviderAccountStatusDto } from '../../services/mediaApi';

const NORMALIZED_STATUS_OPTIONS = [
    { value: '', label: 'All statuses' },
    { value: 'current', label: 'Watching / Reading' },
    { value: 'completed', label: 'Completed' },
    { value: 'planning', label: 'Planning' },
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
    availableListNames: string[];
    providerStatus: MediaProviderAccountStatusDto | null;
    isRefreshing: boolean;
    isPrimaryProviderSelected: boolean;
    onUpdateFilter: <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => void;
    onUpdateProviderFilter: (provider: string | undefined) => void;
    onToggleSortDir: () => void;
    onRefreshFromRemote: () => Promise<void>;
}

// fallow-ignore-next-line complexity
export function LibraryFiltersPanel({
    filters,
    availableListNames,
    providerStatus,
    isRefreshing,
    isPrimaryProviderSelected,
    onUpdateFilter,
    onUpdateProviderFilter,
    onToggleSortDir,
    onRefreshFromRemote,
}: LibraryFiltersPanelProps) {
    const providerOptions: FilterOption[] = [
        { value: '', label: 'All providers' },
        ...mediaProviderCatalog.map((provider) => ({ value: provider.id, label: provider.name })),
    ];
    const listOptions: FilterOption[] = [
        { value: '', label: 'All lists' },
        ...availableListNames.map((listName) => ({ value: listName, label: listName })),
    ];

    return (
        <GlassCard className="p-4">
            <div className="flex flex-wrap items-end gap-3">
                <LibraryFilterFields
                    statusValue={filters.status ?? ''}
                    mediaKindValue={filters.mediaKind ?? ''}
                    providerValue={filters.provider ?? ''}
                    listNameValue={filters.listName ?? ''}
                    providerOptions={providerOptions}
                    listOptions={listOptions}
                    normalizedStatusOptions={NORMALIZED_STATUS_OPTIONS}
                    mediaKindOptions={MEDIA_KIND_OPTIONS}
                    isListDisabled={availableListNames.length === 0 || !isPrimaryProviderSelected}
                    sortBy={filters.sortBy ?? 'updatedAt'}
                    sortDir={filters.sortDir ?? 'desc'}
                    onStatusChange={(value) => onUpdateFilter('status', value)}
                    onMediaKindChange={(value) => onUpdateFilter('mediaKind', value)}
                    onProviderChange={onUpdateProviderFilter}
                    onListChange={(value) => onUpdateFilter('listName', value)}
                    onSortByChange={(value) => onUpdateFilter('sortBy', value)}
                    onToggleSortDir={onToggleSortDir}
                />

                <LibraryRefreshAction
                    isConnected={Boolean(providerStatus?.isConnected)}
                    isRefreshing={isRefreshing}
                    onRefresh={onRefreshFromRemote}
                />
            </div>
        </GlassCard>
    );
}
