import { useEffect, type ReactNode } from 'react';
import { CatalogSearchSection } from '../components/media-library/CatalogSearchSection';
import { LibraryContentSection } from '../components/media-library/LibraryContentSection';
import { LibraryFiltersPanel } from '../components/media-library/LibraryFiltersPanel';
import { LibrarySearchBar } from '../components/media-library/LibrarySearchBar';
import {
  MediaLibraryHeader,
  MediaLibraryRefreshErrorNotice,
} from '../components/media-library/MediaLibraryHeader';
import { useLibrarySearchState } from '../components/media-library/useLibrarySearchState';
import { useMediaLibraryState } from '../components/media-library/useMediaLibraryState';
import type { LibrarySearchMode } from '../components/media-library/LibrarySearchBar';
import type { MediaLibraryFilterDefaults } from '../components/media-library/useMediaLibraryState';

interface MediaLibraryPageProps {
  onNavigateProviders?: () => void;
  onNavigateEntry: (id: string) => void;
  onNavigateCatalogResult?: (providerId: string, providerMediaId: string) => void;
  onHeadingChange?: (heading: MediaPageHeading) => void;
  navigation?: ReactNode;
  density?: MediaLibraryDensity;
  embedded?: boolean;
  searchQuery?: string;
  searchMode?: LibrarySearchMode;
  filterDefaults?: MediaLibraryFilterDefaults;
  onSearchQueryChange?: (query: string) => void;
  onSearchModeChange?: (mode: LibrarySearchMode) => void;
}

export interface MediaPageHeading {
  eyebrow: string;
  title: string;
  details?: string[];
}

export type MediaLibraryDensity = 'comfortable' | 'compact';

type LibraryState = ReturnType<typeof useMediaLibraryState>;
type SearchState = ReturnType<typeof useLibrarySearchState>;

function defaultCatalogNavigation(providerId: string, providerMediaId: string) {
  window.location.assign(`/media/catalog/${encodeURIComponent(providerId)}/${encodeURIComponent(providerMediaId)}`);
}

function LibraryToolbar({
  library,
  search,
  navigation,
  onNavigateProviders,
}: {
  library: LibraryState;
  search: SearchState;
  navigation?: ReactNode;
  onNavigateProviders?: () => void;
}) {
  return (
    <>
      <LibrarySearchBar
        query={search.searchQuery}
        mode={search.searchMode}
        connectedProviderIds={search.connectedProviderIds}
        isSearchingProvider={search.isCatalogSearching}
        navigation={navigation}
        onQueryChange={search.setSearchQuery}
        onModeChange={search.handleSearchModeChange}
        onSubmit={search.handleSearchSubmit}
      />

      {search.searchMode === 'library' ? (
        <LibraryFiltersPanel
          filters={library.filters}
          availableProviderListNames={library.availableProviderListNames}
          providerStatus={library.providerStatus}
          isRefreshing={library.isRefreshing}
          isPrimaryProviderSelected={library.isPrimaryProviderSelected}
          onUpdateFilter={library.updateFilter}
          onUpdateProviderFilter={library.updateProviderFilter}
          onToggleSortDir={library.toggleSortDir}
          onRefreshFromRemote={library.refreshFromRemote}
          onNavigateProviders={onNavigateProviders}
        />
      ) : null}
    </>
  );
}

function LibrarySearchResults({
  library,
  search,
  onNavigateEntry,
  onNavigateProviders,
  onNavigateCatalogResult,
  density,
}: {
  library: LibraryState;
  search: SearchState;
  onNavigateEntry: (id: string) => void;
  onNavigateProviders?: () => void;
  onNavigateCatalogResult?: (providerId: string, providerMediaId: string) => void;
  density: MediaLibraryDensity;
}) {
  if (search.searchMode !== 'library') {
    return (
      <CatalogSearchSection
        providerName={search.providerName}
        query={search.searchQuery.trim()}
        results={search.catalogResults}
        isLoading={search.isCatalogSearching}
        error={search.catalogSearchError}
        hasSearched={search.hasCatalogSearched}
        onRetry={() => void search.runCatalogSearch(search.searchMode, search.searchQuery)}
        onNavigateCatalogResult={onNavigateCatalogResult ?? defaultCatalogNavigation}
        onNavigateEntry={onNavigateEntry}
      />
    );
  }

  return (
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
  );
}

function MediaLibraryContent({
  library,
  search,
  navigation,
  onHeadingChange,
  onNavigateProviders,
  onNavigateEntry,
  onNavigateCatalogResult,
  density,
}: {
  library: LibraryState;
  search: SearchState;
  navigation?: ReactNode;
  onHeadingChange?: (heading: MediaPageHeading) => void;
  onNavigateProviders?: () => void;
  onNavigateEntry: (id: string) => void;
  onNavigateCatalogResult?: (providerId: string, providerMediaId: string) => void;
  density: MediaLibraryDensity;
}) {
  useEffect(() => {
    onHeadingChange?.({
      eyebrow: 'Cantaro · Media',
      title: 'My Library',
    });
  }, [onHeadingChange]);

  return (
    <>
      {!onHeadingChange ? (
        <MediaLibraryHeader
          navigation={navigation}
        />
      ) : null}
      <MediaLibraryRefreshErrorNotice error={library.refreshError} />
      <LibraryToolbar
        library={library}
        search={search}
        navigation={navigation}
        onNavigateProviders={onNavigateProviders}
      />
      <LibrarySearchResults
        library={library}
        search={search}
        onNavigateEntry={onNavigateEntry}
        onNavigateProviders={onNavigateProviders}
        onNavigateCatalogResult={onNavigateCatalogResult}
        density={density}
      />
    </>
  );
}

export function MediaLibraryPage({
  onNavigateProviders,
  onNavigateEntry,
  onNavigateCatalogResult,
  onHeadingChange,
  navigation,
  density = 'comfortable',
  embedded = false,
  searchQuery,
  searchMode,
  filterDefaults,
  onSearchQueryChange,
  onSearchModeChange,
}: MediaLibraryPageProps) {
  const library = useMediaLibraryState(filterDefaults);
  const search = useLibrarySearchState({
    filters: library.filters,
    providerId: library.providerStatus?.providerId,
    isProviderConnected: Boolean(library.providerStatus?.isConnected),
    updateFilter: library.updateFilter,
    searchQuery,
    searchMode,
    onSearchQueryChange,
    onSearchModeChange,
  });
  const contentClassName = `space-y-4 ${embedded ? '' : 'relative z-10 mx-auto max-w-384 px-6 pt-8 pb-16'}`;

  const content = (
    <div className={contentClassName}>
      <MediaLibraryContent
        library={library}
        search={search}
        navigation={navigation}
        onHeadingChange={onHeadingChange}
        onNavigateProviders={onNavigateProviders}
        onNavigateEntry={onNavigateEntry}
        onNavigateCatalogResult={onNavigateCatalogResult}
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
