import { useEffect, useState } from 'react';
import { ArrowDown, ArrowUp, RefreshCw, TriangleAlert } from 'lucide-react';
import { mediaProviderCatalog } from '../../services/mediaProviders';

export interface FilterOption {
  value: string;
  label: string;
}

const selectClassName = 'h-9 w-full min-w-0 border border-border-strong bg-surface px-2 text-sm font-semibold text-content outline-none transition-colors hover:bg-surface-hover focus:border-focus focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';

interface LibraryFilterSelectProps {
  label: string;
  widthClassName?: 'w-30' | 'w-44';
  value?: string;
  options: FilterOption[];
  onChange: (value: string | undefined) => void;
}

export function LibraryFilterSelect({ label, widthClassName = 'w-30', value = '', options, onChange }: LibraryFilterSelectProps) {
  return (
    <label className={`${widthClassName} shrink-0`}>
      <span className="sr-only">{label}</span>
      <select value={value} onChange={(event) => onChange(event.target.value || undefined)} className={selectClassName}>
        {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
    </label>
  );
}

interface LibrarySortControlsProps {
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
  onSortByChange: (value: string) => void;
  onToggleSortDir: () => void;
}

export function LibrarySortControls({ sortBy = 'updatedAt', sortDir = 'desc', onSortByChange, onToggleSortDir }: LibrarySortControlsProps) {
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

export interface LibraryRefreshActionProps {
  canRefresh: boolean;
  syncingProviderIds: readonly string[];
  reconnectProviderId?: string;
  onReconnect?: (providerId: string) => void;
  onRefresh: () => Promise<void>;
}

function providerName(providerId: string): string {
  return mediaProviderCatalog.find(provider => provider.id === providerId)?.name ?? providerId;
}

export function LibraryRefreshAction({ canRefresh, syncingProviderIds, reconnectProviderId, onReconnect, onRefresh }: LibraryRefreshActionProps) {
  const [cycle, setCycle] = useState(0);
  const pendingCount = syncingProviderIds.length;
  useEffect(() => {
    if (pendingCount < 2) return;
    const timer = window.setInterval(() => setCycle(current => current + 1), 2_000);
    return () => window.clearInterval(timer);
  }, [pendingCount]);

  const buttonClassName = 'inline-flex h-9 shrink-0 items-center gap-2 px-2 text-sm font-semibold transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';
  if (pendingCount > 0) {
    const pendingNames = syncingProviderIds.map(providerName);
    return (
      <span className={`${buttonClassName} text-content-muted`}>
        <RefreshCw className="size-4 shrink-0 motion-safe:animate-spin" aria-hidden />
        <span aria-hidden>Syncing {pendingNames[cycle % pendingCount]}…</span>
        <span className="sr-only" role="status">Syncing {pendingNames.join(', ')}</span>
      </span>
    );
  }
  if (reconnectProviderId && onReconnect) {
    const label = `Reconnect ${providerName(reconnectProviderId)}`;
    return (
      <button type="button" className={`${buttonClassName} w-9 justify-center text-warning-content`}
        onClick={() => onReconnect(reconnectProviderId)} aria-label={label} title={label}>
        <TriangleAlert className="size-4" aria-hidden />
      </button>
    );
  }
  if (!canRefresh) return null;
  return (
    <button
      type="button"
      className={`${buttonClassName} text-content-muted hover:text-content`}
      onClick={() => void onRefresh()}
      title="Reload connected provider libraries"
    >
      <RefreshCw className="size-4" aria-hidden />
      Reload
    </button>
  );
}
