import type { MediaLibraryQueryParams } from '../../services/mediaApi';
import { mainMediaProviderId } from '../../services/mediaProviders';

const DEFAULT_LIBRARY_STATUS = 'current';

export function definedMediaLibraryFilterDefaults(
  filterDefaults?: Partial<MediaLibraryQueryParams>,
): Partial<MediaLibraryQueryParams> {
  return Object.fromEntries(
    Object.entries(filterDefaults ?? {}).filter(([, value]) => value !== undefined),
  ) as Partial<MediaLibraryQueryParams>;
}

export function createInitialMediaLibraryFilters(
  filterDefaults?: Partial<MediaLibraryQueryParams>,
): MediaLibraryQueryParams {
  return {
    provider: mainMediaProviderId,
    status: DEFAULT_LIBRARY_STATUS,
    sortBy: 'updatedAt',
    sortDir: 'desc',
    page: 1,
    pageSize: 24,
    ...definedMediaLibraryFilterDefaults(filterDefaults),
  };
}
