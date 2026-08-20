import { useEffect } from 'react';
import { LibraryContentSection } from '../components/media-library/LibraryContentSection';
import { LibraryFiltersPanel } from '../components/media-library/LibraryFiltersPanel';
import { MediaLibraryRefreshErrorNotice } from '../components/media-library/MediaLibraryHeader';
import { useLibrarySearchState } from '../components/media-library/useLibrarySearchState';
import { useMediaLibraryState } from '../components/media-library/useMediaLibraryState';
import type { MediaLibraryFilterDefaults } from '../components/media-library/useMediaLibraryState';

interface MediaLibraryPageProps {
  onNavigateProviders?: () => void;
  onNavigateEntry: (id: string) => void;
  onHeadingChange?: (heading: MediaPageHeading) => void;
  density?: MediaLibraryDensity;
  embedded?: boolean;
  searchQuery?: string;
  filterDefaults?: MediaLibraryFilterDefaults;
  onSearchQueryChange?: (query: string) => void;
}

export interface MediaPageHeading {
  eyebrow: string;
  title: string;
  details?: string[];
  hidden?: boolean;
}

export type MediaLibraryDensity = 'comfortable' | 'compact';

export function MediaLibraryPage({
  onNavigateProviders,
  onNavigateEntry,
  onHeadingChange,
  density = 'comfortable',
  embedded = false,
  searchQuery,
  filterDefaults,
  onSearchQueryChange,
}: MediaLibraryPageProps) {
  const library = useMediaLibraryState(filterDefaults);
  const search = useLibrarySearchState({
    filters: library.filters,
    updateFilter: library.updateFilter,
    searchQuery,
    onSearchQueryChange,
  });

  useEffect(() => {
    onHeadingChange?.({ eyebrow: '', title: '', hidden: true });
  }, [onHeadingChange]);

  const contentClassName = `space-y-6 ${embedded ? '' : 'relative z-10 mx-auto max-w-384 px-4 pt-6 pb-16 sm:px-6 sm:pt-8'}`;

  const content = (
    <div className={contentClassName}>
      <MediaLibraryRefreshErrorNotice error={library.refreshError} />
      <LibraryFiltersPanel
        searchQuery={search.searchQuery}
        filters={library.filters}
        providerStatus={library.providerStatus}
        isRefreshing={library.isRefreshing}
        onSearchQueryChange={search.setSearchQuery}
        onUpdateFilter={library.updateFilter}
        onUpdateProviderFilter={library.updateProviderFilter}
        onToggleSortDir={library.toggleSortDir}
        onRefreshFromRemote={library.refreshFromRemote}
      />
      <LibraryContentSection
        error={library.error}
        isLoading={library.isLoading}
        items={library.items}
        filters={library.filters}
        hasActiveFilters={library.hasActiveFilters}
        totalPages={library.totalPages}
        onRetry={() => library.loadLibrary(library.filters)}
        onNavigateEntry={onNavigateEntry}
        onNavigateProviders={onNavigateProviders}
        onPreviousPage={library.goToPreviousPage}
        onNextPage={library.goToNextPage}
        density={density}
      />
    </div>
  );

  if (embedded) return content;

  return (
    <div className="relative min-h-screen overflow-hidden bg-canvas text-content">
      {content}
    </div>
  );
}
