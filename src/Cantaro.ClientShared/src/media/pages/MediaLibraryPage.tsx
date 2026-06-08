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

interface MediaLibraryPageProps {
  onNavigateProviders?: () => void;
  onNavigateEntry: (id: string) => void;
  onNavigateCatalogResult?: (providerId: string, providerMediaId: string) => void;
  onHeadingChange?: (heading: MediaPageHeading) => void;
  navigation?: ReactNode;
  density?: MediaLibraryDensity;
  embedded?: boolean;
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
          availableListNames={library.availableListNames}
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
}: MediaLibraryPageProps) {
  const library = useMediaLibraryState();
  const search = useLibrarySearchState({
    filters: library.filters,
    providerId: library.providerStatus?.providerId,
    isProviderConnected: Boolean(library.providerStatus?.isConnected),
    updateFilter: library.updateFilter,
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
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />
      {content}
    </div>
  );
}
