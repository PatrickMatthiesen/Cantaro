import { useId, useState } from 'react';
import { ChevronDown, ChevronUp, Search, SlidersHorizontal, X } from 'lucide-react';
import { readStoredValue, writeStoredValue } from '../../services/mediaRefreshCache';
import {
  LibraryFilterSelect,
  LibraryRefreshAction,
  LibrarySortControls,
} from './LibraryFilterControls';
import type { FilterOption, LibraryRefreshActionProps } from './LibraryFilterControls';
import { mediaLibraryStatusLabel } from './mediaLibraryStatus';
import { mediaFormatLabel } from '../../services/mediaFormatting';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import type { MediaLibraryQueryParams } from '../../services/mediaApi';

const COLLECTION_OPTIONS = [
  { value: 'film-tv', label: 'Film & TV' },
  { value: 'anime', label: 'Anime' },
  { value: 'manga', label: 'Manga' },
  { value: 'books', label: 'Books' },
] as const;

const PROVIDER_OPTIONS = [
  { value: '', label: 'All providers' },
  ...mediaProviderCatalog.map((provider) => ({ value: provider.id, label: provider.name })),
];

const STATUS_VALUES = ['', 'current', 'completed', 'planned', 'paused', 'dropped', 'repeating'];

const FORMAT_VALUES: Record<string, string[]> = {
  'film-tv': ['tv', 'tv_short', 'movie', 'ova', 'ona', 'special', 'music'],
  anime: ['tv', 'tv_short', 'movie', 'ova', 'ona', 'special', 'music'],
  manga: ['manga', 'one_shot'],
  books: ['novel'],
};

function statusOptions(collection: string): FilterOption[] {
  const readingKind = collection === 'manga' ? 'manga' : collection === 'books' ? 'lightNovel' : 'anime';
  return STATUS_VALUES.map((value) => ({
    value,
    label: value ? mediaLibraryStatusLabel(value, readingKind) : 'All statuses',
  }));
}

function formatOptions(collection: string): FilterOption[] {
  return [
    { value: '', label: 'All formats' },
    ...(FORMAT_VALUES[collection] ?? FORMAT_VALUES['film-tv'] ?? []).map((value) => ({
      value,
      label: mediaFormatLabel(value),
    })),
  ];
}

export interface LibraryFiltersPanelProps {
  searchQuery: string;
  filters: MediaLibraryQueryParams;
  refreshAction: LibraryRefreshActionProps;
  onSearchQueryChange: (query: string) => void;
  onUpdateFilter: <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => void;
  onCollectionChange: (collection: string) => void;
  onClearAdvancedFilters: () => void;
  onUpdateProviderFilter: (provider: string | undefined) => void;
  onToggleSortDir: () => void;
}

function LibraryCollectionControl({ collection, onCollectionChange }: {
  collection: string;
  onCollectionChange: LibraryFiltersPanelProps['onCollectionChange'];
}) {
  return (
    <>
      <label className="@min-[28rem]/library:hidden">
        <span className="sr-only">Collection</span>
        <select value={collection} onChange={(event) => onCollectionChange(event.target.value)} className="h-9 w-30 border border-border-strong bg-surface px-2 text-sm font-semibold text-content">
          {COLLECTION_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>
      </label>
      <div role="group" aria-label="Collection" className="hidden h-9 shrink-0 overflow-hidden border border-border-strong @min-[28rem]/library:inline-flex">
        {COLLECTION_OPTIONS.map((option) => (
          <button key={option.value} type="button" aria-pressed={collection === option.value} onClick={() => onCollectionChange(option.value)}
            className={`h-full whitespace-nowrap px-3 text-sm font-semibold transition-colors focus-visible:z-10 focus-visible:outline-2 focus-visible:outline-focus [&:not(:last-child)]:border-r [&:not(:last-child)]:border-border-strong ${collection === option.value ? 'bg-personal-accent text-personal-accent-content' : 'text-content-muted hover:bg-surface-hover hover:text-content'}`}>
            {option.label}
          </button>
        ))}
      </div>
    </>
  );
}

function LibrarySearchControl({ searchQuery, onSearchQueryChange }: Pick<LibraryFiltersPanelProps, 'searchQuery' | 'onSearchQueryChange'>) {
  return (
    <label className="flex h-10 items-center gap-2 border border-border-strong bg-surface px-3 focus-within:border-focus">
      <Search className="size-4 shrink-0 text-content-muted" aria-hidden />
      <span className="sr-only">Search library</span>
      <input type="search" value={searchQuery} onChange={(event) => onSearchQueryChange(event.target.value)} placeholder="Search library…"
        className="h-full min-w-0 flex-1 bg-transparent text-sm text-content outline-none placeholder:text-content-subtle" />
      {searchQuery ? <button type="button" aria-label="Clear search" onClick={() => onSearchQueryChange('')} className="flex size-8 items-center justify-center text-content-muted hover:text-content"><X className="size-4" aria-hidden /></button> : null}
    </label>
  );
}

function LibraryFiltersToggle({ open, panelId, activeCount, onToggle }: {
  open: boolean;
  panelId: string;
  activeCount: number;
  onToggle: () => void;
}) {
  const FiltersIcon = open ? ChevronUp : ChevronDown;
  return (
    <button type="button" aria-expanded={open} aria-controls={panelId} onClick={onToggle}
      className="inline-flex h-9 shrink-0 items-center gap-2 px-2 text-sm font-semibold text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus">
      <SlidersHorizontal className="size-4" aria-hidden />
      Filters {activeCount > 0 && <span className="text-xs">{activeCount}</span>}
      <FiltersIcon className="size-4" aria-hidden />
    </button>
  );
}

const filtersOpenKey = 'cantaro.media.library.filtersOpen';

export function LibraryFiltersPanel({
  searchQuery, filters, refreshAction, onSearchQueryChange,
  onUpdateFilter, onCollectionChange, onClearAdvancedFilters,
  onUpdateProviderFilter, onToggleSortDir,
}: LibraryFiltersPanelProps) {
  const collection = filters.collection ?? 'film-tv';
  const [filtersOpen, setFiltersOpen] = useState(() => readStoredValue(filtersOpenKey) === 'true');
  const panelId = useId();
  const activeCount = [searchQuery, filters.status, filters.format, filters.provider].filter(Boolean).length;
  const showCollapsedRefresh = refreshAction.syncingProviderIds.length > 0 || Boolean(refreshAction.reconnectProviderId);
  const toggleFilters = () => {
    const next = !filtersOpen;
    setFiltersOpen(next);
    writeStoredValue(filtersOpenKey, String(next));
  };
  const statusControl = <LibraryFilterSelect label="Status" value={filters.status} options={statusOptions(collection)} onChange={(value) => onUpdateFilter('status', value)} />;
  const formatControl = <LibraryFilterSelect label="Format" value={filters.format} options={formatOptions(collection)} onChange={(value) => onUpdateFilter('format', value)} />;
  const sortControls = <LibrarySortControls sortBy={filters.sortBy} sortDir={filters.sortDir} onSortByChange={(value) => onUpdateFilter('sortBy', value)} onToggleSortDir={onToggleSortDir} />;

  return (
    <section aria-label="Library filters" className="@container/library border-y border-border-subtle py-2">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <LibraryCollectionControl collection={collection} onCollectionChange={onCollectionChange} />
        <div className="ml-auto flex shrink-0 items-center gap-2">
          {!filtersOpen && (
            <>
              {showCollapsedRefresh && <LibraryRefreshAction {...refreshAction} />}
              <div className="hidden @min-[36rem]/library:contents">{statusControl}</div>
              <div className="hidden @min-[44rem]/library:contents">{formatControl}</div>
              <div className="hidden @min-[52rem]/library:contents">{sortControls}</div>
            </>
          )}
          <LibraryFiltersToggle open={filtersOpen} panelId={panelId} activeCount={activeCount} onToggle={toggleFilters} />
        </div>
      </div>
      {filtersOpen && (
        <div id={panelId} className="mt-3 space-y-3 border-t border-border-subtle pt-3">
          <LibrarySearchControl searchQuery={searchQuery} onSearchQueryChange={onSearchQueryChange} />
          <div className="flex flex-wrap items-center gap-2">
            {statusControl}
            {formatControl}
            {sortControls}
            <LibraryFilterSelect label="Provider" widthClassName="w-44" value={filters.provider} options={PROVIDER_OPTIONS} onChange={onUpdateProviderFilter} />
            <LibraryRefreshAction {...refreshAction} />
            <button type="button" onClick={() => { onSearchQueryChange(''); onClearAdvancedFilters(); }} className="ml-auto h-9 px-2 text-sm text-content-muted hover:text-content focus-visible:outline-2 focus-visible:outline-focus">Clear filters</button>
          </div>
        </div>
      )}
    </section>
  );
}
