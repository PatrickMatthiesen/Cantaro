interface FilterOption {
    value: string;
    label: string;
}

interface FilterSelectProps {
    label: string;
    value: string;
    options: FilterOption[];
    onChange: (value: string | undefined) => void;
    disabled?: boolean;
}

interface SortControlsProps {
    sortBy: string;
    sortDir: 'asc' | 'desc';
    onSortByChange: (value: string) => void;
    onToggleSortDir: () => void;
}

export interface LibraryFilterFieldsProps {
    statusValue: string;
    mediaKindValue: string;
    providerValue: string;
    listNameValue: string;
    providerOptions: FilterOption[];
    listOptions: FilterOption[];
    normalizedStatusOptions: FilterOption[];
    mediaKindOptions: FilterOption[];
    isListDisabled: boolean;
    sortBy: string;
    sortDir: 'asc' | 'desc';
    onStatusChange: (value: string | undefined) => void;
    onMediaKindChange: (value: string | undefined) => void;
    onProviderChange: (value: string | undefined) => void;
    onListChange: (value: string | undefined) => void;
    onSortByChange: (value: string) => void;
    onToggleSortDir: () => void;
}

export interface LibraryRefreshActionProps {
    isConnected: boolean;
    isRefreshing: boolean;
    onRefresh: () => Promise<void>;
    onNavigateProviders?: () => void;
}

function FilterSelect({ label, value, options, onChange, disabled = false }: FilterSelectProps) {
    return (
        <div className="flex flex-col gap-1">
            <label className="text-xs font-medium tracking-wide text-gray-500 uppercase">{label}</label>
            <select
                className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:ring-2 focus:ring-indigo-400 focus:outline-none disabled:cursor-not-allowed disabled:opacity-60"
                value={value}
                onChange={(event) => onChange(event.target.value || undefined)}
                disabled={disabled}
            >
                {options.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                ))}
            </select>
        </div>
    );
}

function SortControls({ sortBy, sortDir, onSortByChange, onToggleSortDir }: SortControlsProps) {
    return (
        <div className="flex flex-col gap-1">
            <label className="text-xs font-medium tracking-wide text-gray-500 uppercase">Sort by</label>
            <div className="flex gap-1">
                <select
                    className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-700 focus:ring-2 focus:ring-indigo-400 focus:outline-none"
                    value={sortBy}
                    onChange={(event) => onSortByChange(event.target.value)}
                >
                    <option value="updatedAt">Last updated</option>
                    <option value="title">Title</option>
                    <option value="status">Status</option>
                    <option value="progress">Progress</option>
                </select>
                <button
                    type="button"
                    className="rounded-xl border border-gray-200 bg-white/80 px-3 py-2 text-sm text-gray-600 transition hover:bg-white"
                    onClick={onToggleSortDir}
                    title={sortDir === 'asc' ? 'Ascending - click to switch' : 'Descending - click to switch'}
                >
                    {sortDir === 'asc' ? '↑' : '↓'}
                </button>
            </div>
        </div>
    );
}

export function LibraryFilterFields({
    statusValue,
    mediaKindValue,
    providerValue,
    listNameValue,
    providerOptions,
    listOptions,
    normalizedStatusOptions,
    mediaKindOptions,
    isListDisabled,
    sortBy,
    sortDir,
    onStatusChange,
    onMediaKindChange,
    onProviderChange,
    onListChange,
    onSortByChange,
    onToggleSortDir,
}: LibraryFilterFieldsProps) {
    return (
        <>
            <FilterSelect label="Status" value={statusValue} options={normalizedStatusOptions} onChange={onStatusChange} />
            <FilterSelect label="Type" value={mediaKindValue} options={mediaKindOptions} onChange={onMediaKindChange} />
            <FilterSelect label="Provider" value={providerValue} options={providerOptions} onChange={onProviderChange} />
            <FilterSelect
                label="List"
                value={listNameValue}
                options={listOptions}
                onChange={onListChange}
                disabled={isListDisabled}
            />
            <SortControls sortBy={sortBy} sortDir={sortDir} onSortByChange={onSortByChange} onToggleSortDir={onToggleSortDir} />
        </>
    );
}

export function LibraryRefreshAction({ isConnected, isRefreshing, onRefresh, onNavigateProviders }: LibraryRefreshActionProps) {
    return (
        <div className="ml-auto flex flex-col gap-1">
            <span className="text-xs font-medium tracking-wide text-gray-500 uppercase">Providers</span>
            <div className="flex gap-2">
                {onNavigateProviders ? (
                    <button
                        type="button"
                        className="inline-flex h-10 items-center justify-center rounded-xl border border-gray-200 bg-white/80 px-4 text-sm font-medium text-gray-700 transition hover:bg-white"
                        onClick={onNavigateProviders}
                    >
                        Manage
                    </button>
                ) : null}
                {isConnected ? (
                    <button
                        type="button"
                        className="inline-flex h-10 items-center justify-center rounded-xl border border-gray-200 bg-white/80 px-4 text-sm font-medium text-gray-700 transition hover:bg-white disabled:cursor-not-allowed disabled:opacity-60"
                        onClick={() => void onRefresh()}
                        disabled={isRefreshing}
                        aria-busy={isRefreshing}
                        title="Reload the primary provider library"
                    >
                        {isRefreshing ? 'Reloading…' : '↻ Reload'}
                    </button>
                ) : null}
            </div>
        </div>
    );
}
