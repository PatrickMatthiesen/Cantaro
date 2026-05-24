import { LibraryContentSection } from '../components/media-library/LibraryContentSection';
import type { ReactNode } from 'react';
import { LibraryFiltersPanel } from '../components/media-library/LibraryFiltersPanel';
import {
  MediaLibraryHeader,
  MediaLibraryRefreshErrorNotice,
} from '../components/media-library/MediaLibraryHeader';
import { useMediaLibraryState } from '../components/media-library/useMediaLibraryState';

interface MediaLibraryPageProps {
  onNavigateProviders?: () => void;
  onNavigateEntry: (id: string) => void;
  navigation?: ReactNode;
  embedded?: boolean;
}

export function MediaLibraryPage({
  onNavigateProviders,
  onNavigateEntry,
  navigation,
  embedded = false,
}: MediaLibraryPageProps) {
  const {
    filters,
    items,
    availableListNames,
    totalPages,
    totalCount,
    isLoading,
    error,
    providerStatus,
    isRefreshing,
    refreshError,
    formattedLastRemoteCheckAt,
    hasActiveFilters,
    isPrimaryProviderSelected,
    updateFilter,
    updateProviderFilter,
    toggleSortDir,
    refreshFromRemote,
    loadLibrary,
    goToPreviousPage,
    goToNextPage,
  } = useMediaLibraryState();

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className={`relative z-10 mx-auto space-y-6 ${embedded ? 'max-w-440 px-4 pt-4 pb-6' : 'max-w-384 px-6 pt-8 pb-16'}`}>
        <MediaLibraryHeader
          totalCount={totalCount}
          isLoading={isLoading}
          isProviderConnected={Boolean(providerStatus?.isConnected)}
          formattedLastRemoteCheckAt={formattedLastRemoteCheckAt}
          navigation={navigation}
        />

        <MediaLibraryRefreshErrorNotice error={refreshError} />

        <LibraryFiltersPanel
          filters={filters}
          availableListNames={availableListNames}
          providerStatus={providerStatus}
          isRefreshing={isRefreshing}
          isPrimaryProviderSelected={isPrimaryProviderSelected}
          onUpdateFilter={updateFilter}
          onUpdateProviderFilter={updateProviderFilter}
          onToggleSortDir={toggleSortDir}
          onRefreshFromRemote={refreshFromRemote}
          onNavigateProviders={onNavigateProviders}
        />

        <LibraryContentSection
          error={error}
          isLoading={isLoading}
          items={items}
          filters={filters}
          hasActiveFilters={hasActiveFilters}
          totalPages={totalPages}
          onRetry={() => loadLibrary(filters)}
          onNavigateEntry={onNavigateEntry}
          onNavigateProviders={onNavigateProviders}
          onPreviousPage={goToPreviousPage}
          onNextPage={goToNextPage}
        />
      </div>
    </div>
  );
}
