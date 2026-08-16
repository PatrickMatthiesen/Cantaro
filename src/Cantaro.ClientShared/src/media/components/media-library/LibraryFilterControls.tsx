import { ArrowDown, ArrowUp, RefreshCw, Settings2 } from 'lucide-react';
import { ActionButton, IconButton, SelectField } from '../../../ui';

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
    providerListNameValue: string;
    providerOptions: FilterOption[];
    listOptions: FilterOption[];
    statusOptions: FilterOption[];
    mediaKindOptions: FilterOption[];
    isListDisabled: boolean;
    sortBy: string;
    sortDir: 'asc' | 'desc';
    onStatusChange: (value: string | undefined) => void;
    onMediaKindChange: (value: string | undefined) => void;
    onProviderChange: (value: string | undefined) => void;
    onProviderListChange: (value: string | undefined) => void;
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
    const containerClassName = label === 'Status' ? 'min-w-40 flex-[1.25]' : 'min-w-28 flex-1';

    return (
        <SelectField
            label={label}
            containerClassName={containerClassName}
            className="w-full"
            value={value}
            onChange={(event) => onChange(event.target.value || undefined)}
            disabled={disabled}
        >
                {options.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                ))}
        </SelectField>
    );
}

function SortControls({ sortBy, sortDir, onSortByChange, onToggleSortDir }: SortControlsProps) {
    return (
        <div className="grid min-w-40 flex-1 gap-1.5 text-sm text-content-muted">
            <span>Sort by</span>
            <div className="flex gap-1.5">
                <SelectField
                    label="Sort library"
                    visuallyHiddenLabel
                    containerClassName="min-w-0 flex-1"
                    className="w-full"
                    value={sortBy}
                    onChange={(event) => onSortByChange(event.target.value)}
                >
                    <option value="updatedAt">Updated</option>
                    <option value="title">Title</option>
                    <option value="status">Status</option>
                    <option value="progress">Progress</option>
                </SelectField>
                <IconButton
                    label={sortDir === 'asc' ? 'Sort ascending; switch to descending' : 'Sort descending; switch to ascending'}
                    className="border border-border-strong bg-surface"
                    onClick={onToggleSortDir}
                >
                    {sortDir === 'asc' ? <ArrowUp className="size-4" aria-hidden /> : <ArrowDown className="size-4" aria-hidden />}
                </IconButton>
            </div>
        </div>
    );
}

export function LibraryFilterFields({
    statusValue,
    mediaKindValue,
    providerValue,
    providerListNameValue,
    providerOptions,
    listOptions,
    statusOptions,
    mediaKindOptions,
    isListDisabled,
    sortBy,
    sortDir,
    onStatusChange,
    onMediaKindChange,
    onProviderChange,
    onProviderListChange,
    onSortByChange,
    onToggleSortDir,
}: LibraryFilterFieldsProps) {
    return (
        <>
            <FilterSelect label="Status" value={statusValue} options={statusOptions} onChange={onStatusChange} />
            <FilterSelect label="Type" value={mediaKindValue} options={mediaKindOptions} onChange={onMediaKindChange} />
            <FilterSelect label="Provider" value={providerValue} options={providerOptions} onChange={onProviderChange} />
            <FilterSelect
                label="Provider list"
                value={providerListNameValue}
                options={listOptions}
                onChange={onProviderListChange}
                disabled={isListDisabled}
            />
            <SortControls sortBy={sortBy} sortDir={sortDir} onSortByChange={onSortByChange} onToggleSortDir={onToggleSortDir} />
        </>
    );
}

export function LibraryRefreshAction({ isConnected, isRefreshing, onRefresh, onNavigateProviders }: LibraryRefreshActionProps) {
    return (
        <div className="grid min-w-fit gap-1.5 text-sm text-content-muted lg:ml-auto">
            <span>Providers</span>
            <div className="flex flex-wrap gap-1.5">
                {onNavigateProviders ? (
                    <ActionButton tone="ghost" onClick={onNavigateProviders}>
                        <Settings2 className="size-4" aria-hidden />
                        Manage
                    </ActionButton>
                ) : null}
                {isConnected ? (
                    <ActionButton
                        tone="ghost"
                        onClick={() => void onRefresh()}
                        disabled={isRefreshing}
                        aria-busy={isRefreshing}
                        busyLabel="Reloading…"
                        title="Reload the primary provider library"
                    >
                        <RefreshCw className="size-4" aria-hidden />
                        Reload
                    </ActionButton>
                ) : null}
            </div>
        </div>
    );
}
