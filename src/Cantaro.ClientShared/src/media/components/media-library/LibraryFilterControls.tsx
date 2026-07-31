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
            <label className="text-xs font-medium tracking-wide text-content-muted uppercase">{label}</label>
            <select
                className="h-9 rounded-xl border border-border-subtle bg-surface-translucent px-3 text-sm text-content focus:ring-2 focus:ring-focus focus:outline-none disabled:cursor-not-allowed disabled:opacity-60"
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
            <label className="text-xs font-medium tracking-wide text-content-muted uppercase">Sort by</label>
            <div className="flex gap-1">
                <select
                    className="h-9 rounded-xl border border-border-subtle bg-surface-translucent px-3 text-sm text-content focus:ring-2 focus:ring-focus focus:outline-none"
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
                    className="flex h-9 w-9 items-center justify-center rounded-xl border border-border-subtle bg-surface-translucent text-sm text-content-muted transition hover:bg-surface-hover"
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
            <span className="text-xs font-medium tracking-wide text-content-muted uppercase">Providers</span>
            <div className="flex gap-1.5">
                {onNavigateProviders ? (
                    <button
                        type="button"
                        className="inline-flex h-9 items-center justify-center rounded-xl border border-border-subtle bg-surface-translucent px-3 text-sm font-medium text-content transition hover:bg-surface-hover"
                        onClick={onNavigateProviders}
                    >
                        Manage
                    </button>
                ) : null}
                {isConnected ? (
                    <button
                        type="button"
                        className="inline-flex h-9 items-center justify-center rounded-xl border border-border-subtle bg-surface-translucent px-3 text-sm font-medium text-content transition hover:bg-surface-hover disabled:cursor-not-allowed disabled:opacity-60"
                        onClick={() => void onRefresh()}
                        disabled={isRefreshing}
                        aria-busy={isRefreshing}
                        title="Reload the primary provider library"
                    >
                        {isRefreshing ? 'Reloading...' : 'Reload'}
                    </button>
                ) : null}
            </div>
        </div>
    );
}
