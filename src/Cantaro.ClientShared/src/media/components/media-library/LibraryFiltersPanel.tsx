import { useEffect, useId, useRef, useState } from 'react';
import { Search, SlidersHorizontal, X } from 'lucide-react';
import {
  LibraryAdvancedFilterFields,
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
    ...(FORMAT_VALUES[collection] ?? FORMAT_VALUES['film-tv']).map((value) => ({
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

// fallow-ignore-next-line complexity
export function LibraryFiltersPanel({
  searchQuery,
  filters,
  providerStatus,
  isRefreshing,
  onSearchQueryChange,
  onUpdateFilter,
  onCollectionChange,
  onClearAdvancedFilters,
  onUpdateProviderFilter,
  onToggleSortDir,
  onRefreshFromRemote,
}: LibraryFiltersPanelProps) {
  const collection = filters.collection ?? 'film-tv';
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(Boolean(searchQuery));
  const searchExpanded = searchOpen || Boolean(searchQuery);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const searchButtonRef = useRef<HTMLButtonElement>(null);
  const filtersButtonRef = useRef<HTMLButtonElement>(null);
  const popoverRootRef = useRef<HTMLDivElement>(null);
  const popoverId = useId();

  useEffect(() => {
    if (searchOpen) searchInputRef.current?.focus();
  }, [searchOpen]);

  useEffect(() => {
    if (!filtersOpen) return;

    const dismissOnOutsideClick = (event: PointerEvent) => {
      if (!popoverRootRef.current?.contains(event.target as Node)) setFiltersOpen(false);
    };
    const dismissOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setFiltersOpen(false);
      filtersButtonRef.current?.focus();
    };
    document.addEventListener('pointerdown', dismissOnOutsideClick);
    document.addEventListener('keydown', dismissOnEscape);
    return () => {
      document.removeEventListener('pointerdown', dismissOnOutsideClick);
      document.removeEventListener('keydown', dismissOnEscape);
    };
  }, [filtersOpen]);

  const providerOptions: FilterOption[] = [
    { value: '', label: 'All providers' },
    ...mediaProviderCatalog.map((provider) => ({ value: provider.id, label: provider.name })),
  ];
  const advancedCount = [filters.status, filters.format, filters.provider].filter(Boolean).length;

  return (
    <section aria-label="Library filters" className="@container/library border-y border-border-subtle py-2">
      <div className="flex items-center gap-3">
        <label className={searchExpanded ? 'hidden' : 'shrink-0 @min-[28rem]/library:hidden'}>
          <span className="sr-only">Collection</span>
          <select value={collection} onChange={(event) => onCollectionChange(event.target.value)} className="h-9 w-30 border border-border-strong bg-surface px-2 text-sm font-semibold text-content">
            {COLLECTION_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </label>
        <div role="group" aria-label="Collection" className={`hidden h-9 shrink-0 overflow-hidden border border-border-strong ${searchExpanded ? '@min-[44rem]/library:inline-flex' : '@min-[28rem]/library:inline-flex'}`}>
          {COLLECTION_OPTIONS.map((option) => {
            const selected = collection === option.value;
            return (
              <button
                key={option.value}
                type="button"
                aria-pressed={selected}
                onClick={() => onCollectionChange(option.value)}
                className={`h-full whitespace-nowrap px-2 text-xs font-semibold transition-colors focus-visible:z-10 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus sm:px-3 sm:text-sm [&:not(:last-child)]:border-r [&:not(:last-child)]:border-border-strong ${selected
                  ? 'bg-personal-accent text-personal-accent-content'
                  : 'text-content-muted hover:bg-surface-hover hover:text-content'}`}
              >
                {option.label}
              </button>
            );
          })}
        </div>

        <div className={`ml-auto flex min-w-0 items-center gap-1.5 ${searchExpanded ? 'flex-1 @min-[44rem]/library:flex-none' : ''}`}>
          <div className={searchExpanded ? 'hidden' : 'hidden @min-[36rem]/library:contents'}>
            <LibraryStatusControl
              value={filters.status ?? ''}
              options={statusOptions(collection)}
              onChange={(value) => onUpdateFilter('status', value)}
            />
          </div>
          <div className={searchExpanded ? 'hidden' : 'contents'}>
            <LibraryFormatControl
              value={filters.format ?? ''}
              options={formatOptions(collection)}
              onChange={(value) => onUpdateFilter('format', value)}
            />
          </div>
          <div className={searchExpanded ? 'hidden' : 'hidden @min-[52rem]/library:contents'}>
            <LibrarySortControls
              sortBy={filters.sortBy ?? 'updatedAt'}
              sortDir={filters.sortDir ?? 'desc'}
              onSortByChange={(value) => onUpdateFilter('sortBy', value)}
              onToggleSortDir={onToggleSortDir}
            />
          </div>

          <div className={`flex min-w-0 items-center ${searchExpanded ? 'flex-1 @min-[44rem]/library:flex-none' : ''}`}>
            <button
              ref={searchButtonRef}
              type="button"
              aria-label={searchExpanded ? 'Focus library search' : 'Search library'}
              aria-expanded={searchExpanded}
              onClick={() => {
                setSearchOpen(true);
                searchInputRef.current?.focus();
              }}
              className="inline-flex size-9 shrink-0 items-center justify-center text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
            >
              <Search className="size-4" aria-hidden />
            </button>
            {searchExpanded ? (
              <>
                <label className="min-w-0 flex-1 @min-[44rem]/library:w-48 @min-[44rem]/library:flex-none">
                  <span className="sr-only">Search library</span>
                  <input
                    ref={searchInputRef}
                    type="search"
                    value={searchQuery}
                    onChange={(event) => onSearchQueryChange(event.target.value)}
                    placeholder="Search library…"
                    className="h-9 w-full min-w-0 border border-border-strong bg-surface px-2 text-sm text-content outline-none placeholder:text-content-subtle hover:bg-surface-hover focus:border-focus focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
                  />
                </label>
                <button
                  type="button"
                  aria-label="Close search"
                  onClick={() => {
                    onSearchQueryChange('');
                    setSearchOpen(false);
                    searchButtonRef.current?.focus();
                  }}
                  className="inline-flex size-9 shrink-0 items-center justify-center text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
                >
                  <X className="size-4" aria-hidden />
                </button>
              </>
            ) : null}
          </div>

          <div ref={popoverRootRef} className="relative shrink-0">
            <button
              ref={filtersButtonRef}
              type="button"
              aria-label={advancedCount ? `Filters, ${advancedCount} active` : 'Filters'}
              aria-expanded={filtersOpen}
              aria-controls={popoverId}
              onClick={() => setFiltersOpen((open) => !open)}
              className={`inline-flex h-9 items-center gap-1.5 px-2 text-sm font-semibold transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${filtersOpen || advancedCount ? 'text-content' : 'text-content-muted'}`}
            >
              <SlidersHorizontal className="size-4" aria-hidden />
              <span className="hidden sm:inline">Filters</span>
              {advancedCount > 0 ? <span className="text-xs">{advancedCount}</span> : null}
            </button>
            {filtersOpen ? (
              <div
                id={popoverId}
                role="group"
                aria-label="Advanced filters"
                className="absolute top-full right-0 z-30 mt-2 w-[min(22rem,calc(100vw-2rem))] border border-border-strong bg-surface p-3 shadow-xl"
              >
                <div className={`mb-3 flex flex-wrap gap-3 ${searchExpanded ? '' : '@min-[52rem]/library:hidden'}`}>
                  <div className={`grid gap-1 text-xs font-semibold text-content-muted ${searchExpanded ? '' : '@min-[36rem]/library:hidden'}`}>
                    <span>Status</span>
                    <LibraryStatusControl value={filters.status ?? ''} options={statusOptions(collection)} onChange={(value) => onUpdateFilter('status', value)} />
                  </div>
                  <div className="grid gap-1 text-xs font-semibold text-content-muted">
                    <span>Sort by</span>
                    <LibrarySortControls sortBy={filters.sortBy ?? 'updatedAt'} sortDir={filters.sortDir ?? 'desc'} onSortByChange={(value) => onUpdateFilter('sortBy', value)} onToggleSortDir={onToggleSortDir} />
                  </div>
                </div>
                <LibraryAdvancedFilterFields
                  showAllFilters={searchExpanded}
                  formatValue={filters.format ?? ''}
                  formatOptions={formatOptions(collection)}
                  providerValue={filters.provider ?? ''}
                  providerOptions={providerOptions}
                  onFormatChange={(value) => onUpdateFilter('format', value)}
                  onProviderChange={onUpdateProviderFilter}
                />
                <div className="mt-3 flex items-center justify-between border-t border-border-subtle pt-2">
                  <button
                    type="button"
                    onClick={onClearAdvancedFilters}
                    className="h-9 px-2 text-sm font-semibold text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
                  >
                    Clear filters
                  </button>
                  <LibraryRefreshAction
                    isConnected={Boolean(providerStatus?.isConnected)}
                    isRefreshing={isRefreshing}
                    onRefresh={onRefreshFromRemote}
                  />
                </div>
              </div>
            ) : null}
          </div>
        </div>
      </div>
    </section>
  );
}
