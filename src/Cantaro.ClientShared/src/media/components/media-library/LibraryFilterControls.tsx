import { ArrowDown, ArrowUp, RefreshCw } from 'lucide-react';

export interface FilterOption {
  value: string;
  label: string;
}

const selectClassName = 'h-9 w-full min-w-0 border border-border-strong bg-surface px-2 text-sm font-semibold text-content outline-none transition-colors hover:bg-surface-hover focus:border-focus focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';

interface LibraryStatusControlProps {
  value: string;
  options: FilterOption[];
  onChange: (value: string | undefined) => void;
}

export function LibraryStatusControl({ value, options, onChange }: LibraryStatusControlProps) {
  return (
    <label className="w-30 shrink-0">
      <span className="sr-only">Status</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value || undefined)}
        className={selectClassName}
      >
        {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
    </label>
  );
}

interface LibrarySortControlsProps {
  sortBy: string;
  sortDir: 'asc' | 'desc';
  onSortByChange: (value: string) => void;
  onToggleSortDir: () => void;
}

export function LibraryFormatControl({ value, options, onChange }: LibraryStatusControlProps) {
  return (
    <label className="w-30 shrink-0">
      <span className="sr-only">Format</span>
      <select value={value} onChange={(event) => onChange(event.target.value || undefined)} className={selectClassName}>
        {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
    </label>
  );
}

export function LibrarySortControls({ sortBy, sortDir, onSortByChange, onToggleSortDir }: LibrarySortControlsProps) {
  return (
    <div className="flex shrink-0 items-center gap-1">
      <label className="w-24 shrink-0">
        <span className="sr-only">Sort library</span>
        <select value={sortBy} onChange={(event) => onSortByChange(event.target.value)} className={selectClassName}>
          <option value="updatedAt">Updated</option>
          <option value="title">Title</option>
          <option value="status">Status</option>
          <option value="progress">Progress</option>
        </select>
      </label>
      <button type="button"
        aria-label={sortDir === 'asc' ? 'Sort ascending; switch to descending' : 'Sort descending; switch to ascending'}
        className="inline-flex size-9 shrink-0 items-center justify-center border border-border-strong bg-surface text-content-muted hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus"
        onClick={onToggleSortDir}
      >
        {sortDir === 'asc' ? <ArrowUp className="size-4" aria-hidden /> : <ArrowDown className="size-4" aria-hidden />}
      </button>
    </div>
  );
}

interface LibraryAdvancedFilterFieldsProps {
  providerValue: string;
  providerOptions: FilterOption[];

  onProviderChange: (value: string | undefined) => void;
}

export function LibraryAdvancedFilterFields({
  providerValue,
  providerOptions,

  onProviderChange,
}: LibraryAdvancedFilterFieldsProps) {
  return (
    <div className="w-44">
      <label className="grid min-w-0 gap-1 text-xs font-semibold text-content-muted">
        Provider
        <select value={providerValue} onChange={(event) => onProviderChange(event.target.value || undefined)} className={selectClassName}>
          {providerOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>
      </label>
    </div>
  );
}

export interface LibraryRefreshActionProps {
  isConnected: boolean;
  isRefreshing: boolean;
  onRefresh: () => Promise<void>;
}

export function LibraryRefreshAction({ isConnected, isRefreshing, onRefresh }: LibraryRefreshActionProps) {
  if (!isConnected) return null;

  return (
    <button
      type="button"
      className="inline-flex h-9 items-center gap-2 px-2 text-sm font-semibold text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-45"
      onClick={() => void onRefresh()}
      disabled={isRefreshing}
      aria-busy={isRefreshing}
      title="Reload the primary provider library"
    >
      <RefreshCw className={`size-4 ${isRefreshing ? 'animate-spin' : ''}`} aria-hidden />
      {isRefreshing ? 'Reloading…' : 'Reload'}
    </button>
  );
}
