import { useId, useState } from 'react';
import { ChevronDown, ChevronUp, Search, SlidersHorizontal, X } from 'lucide-react';
import { readStoredValue, writeStoredValue } from '../../services/mediaRefreshCache';
import {
  LibraryProviderControl,
  LibraryFormatControl,
  LibraryRefreshAction,
  LibrarySortControls,
  LibraryStatusControl,
} from './LibraryFilterControls';
import type { FilterOption } from './LibraryFilterControls';
import { mediaLibraryStatusLabel } from './mediaLibraryStatus';
import { mediaFormatLabel } from '../../services/mediaFormatting';
import { mediaProviderCatalog } from '../../services/mediaProviders';
import type { MediaLibraryQueryParams, MediaProviderAccountStatusDto } from '../../services/mediaApi';

const COLLECTION_OPTIONS = [
  { value: 'film-tv', label: 'Film & TV' },
  { value: 'anime', label: 'Anime' },
  { value: 'manga', label: 'Manga' },
  { value: 'books', label: 'Books' },
] as const;

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
  providerStatus: MediaProviderAccountStatusDto | null;
  isRefreshing: boolean;
  onSearchQueryChange: (query: string) => void;
  onUpdateFilter: <K extends keyof MediaLibraryQueryParams>(key: K, value: MediaLibraryQueryParams[K]) => void;
  onCollectionChange: (collection: string) => void;
  onClearAdvancedFilters: () => void;
  onUpdateProviderFilter: (provider: string | undefined) => void;
  onToggleSortDir: () => void;
  onRefreshFromRemote: () => Promise<void>;
}

const filtersOpenKey = 'cantaro.media.library.filtersOpen';

export function LibraryFiltersPanel({
  searchQuery, filters, providerStatus, isRefreshing, onSearchQueryChange,
  onUpdateFilter, onCollectionChange, onClearAdvancedFilters,
  onUpdateProviderFilter, onToggleSortDir, onRefreshFromRemote,
}: LibraryFiltersPanelProps) {
  const collection = filters.collection ?? 'film-tv';
  const [filtersOpen, setFiltersOpen] = useState(() => readStoredValue(filtersOpenKey) === 'true');
  const panelId = useId();
  const activeCount = [searchQuery, filters.status, filters.format, filters.provider].filter(Boolean).length;
  const providerOptions = [
    { value: '', label: 'All providers' },
    ...mediaProviderCatalog.map((provider) => ({ value: provider.id, label: provider.name })),
  ];
  const toggleFilters = () => {
    const next = !filtersOpen;
    setFiltersOpen(next);
    writeStoredValue(filtersOpenKey, String(next));
  };
  const statusControl = <LibraryStatusControl value={filters.status ?? ''} options={statusOptions(collection)} onChange={(value) => onUpdateFilter('status', value)} />;
  const formatControl = <LibraryFormatControl value={filters.format ?? ''} options={formatOptions(collection)} onChange={(value) => onUpdateFilter('format', value)} />;
  const sortControls = <LibrarySortControls sortBy={filters.sortBy ?? 'updatedAt'} sortDir={filters.sortDir ?? 'desc'} onSortByChange={(value) => onUpdateFilter('sortBy', value)} onToggleSortDir={onToggleSortDir} />;

  return (
    <section aria-label="Library filters" className="@container/library border-y border-border-subtle py-2">
      <div className="flex items-center justify-between gap-3">
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
        <div className="ml-auto flex shrink-0 items-center gap-2">
          {!filtersOpen ? (
            <>
              <div className="hidden @min-[36rem]/library:contents">{statusControl}</div>
              <div className="hidden @min-[44rem]/library:contents">{formatControl}</div>
              <div className="hidden @min-[52rem]/library:contents">{sortControls}</div>
            </>
          ) : null}
        <button type="button" aria-expanded={filtersOpen} aria-controls={panelId} onClick={toggleFilters}
          className="inline-flex h-9 shrink-0 items-center gap-2 px-2 text-sm font-semibold text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus">
          <SlidersHorizontal className="size-4" aria-hidden />
          Filters {activeCount > 0 ? <span className="text-xs">{activeCount}</span> : null}
          {filtersOpen ? <ChevronUp className="size-4" aria-hidden /> : <ChevronDown className="size-4" aria-hidden />}
        </button>
        </div>
      </div>
      {filtersOpen ? (
        <div id={panelId} className="mt-3 space-y-3 border-t border-border-subtle pt-3">
          <label className="flex h-10 items-center gap-2 border border-border-strong bg-surface px-3 focus-within:border-focus">
            <Search className="size-4 shrink-0 text-content-muted" aria-hidden />
            <span className="sr-only">Search library</span>
            <input type="search" value={searchQuery} onChange={(event) => onSearchQueryChange(event.target.value)} placeholder="Search library…"
              className="h-full min-w-0 flex-1 bg-transparent text-sm text-content outline-none placeholder:text-content-subtle" />
            {searchQuery ? <button type="button" aria-label="Clear search" onClick={() => onSearchQueryChange('')} className="flex size-8 items-center justify-center text-content-muted hover:text-content"><X className="size-4" aria-hidden /></button> : null}
          </label>
          <div className="flex flex-wrap items-center gap-2">
            {statusControl}
            {formatControl}
            {sortControls}
            <LibraryProviderControl providerValue={filters.provider ?? ''} providerOptions={providerOptions} onProviderChange={onUpdateProviderFilter} />
            <LibraryRefreshAction isConnected={Boolean(providerStatus?.isConnected)} isRefreshing={isRefreshing} onRefresh={onRefreshFromRemote} />
            <button type="button" onClick={() => { onSearchQueryChange(''); onClearAdvancedFilters(); }} className="ml-auto h-9 px-2 text-sm text-content-muted hover:text-content focus-visible:outline-2 focus-visible:outline-focus">Clear filters</button>
          </div>
        </div>
      ) : null}
    </section>
  );
}
