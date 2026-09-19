import type { MediaLibraryQueryParams } from '@cantaro/client-shared/media';

function readOptionalSearchString(search: Record<string, unknown>, key: string) {
  return typeof search[key] === 'string' && search[key] ? search[key] as string : undefined;
}

function readSortDir(search: Record<string, unknown>) {
  return search.sortDir === 'asc' || search.sortDir === 'desc' ? search.sortDir : undefined;
}

function hasMediaFilterDefaults(search: Record<string, unknown>) {
  return ['q', 'collection', 'format', 'status', 'mediaKind', 'provider', 'providerListName', 'sortBy', 'sortDir', 'page'].some((key) => key in search);
}

function normalizeCollectionFilters(filters: Partial<MediaLibraryQueryParams>) {
  if (filters.collection) {
    delete filters.mediaKind;
    return filters;
  }
  const legacy: Record<string, { collection: string; format?: string }> = {
    anime: { collection: 'anime' },
    manga: { collection: 'manga' },
    lightNovel: { collection: 'books', format: 'novel' },
    oneShot: { collection: 'manga', format: 'one_shot' },
    movie: { collection: 'film-tv', format: 'movie' },
    series: { collection: 'film-tv', format: 'tv' },
  };
  const mapped = legacy[filters.mediaKind ?? ''];
  if (mapped) {
    Object.assign(filters, mapped);
    delete filters.mediaKind;
  }
  return filters;
}

export function getMediaFilterDefaults(search: Record<string, unknown>): Partial<MediaLibraryQueryParams> | undefined {
  if (!hasMediaFilterDefaults(search)) return undefined;

  const page = Number(search.page);
  const filters: Partial<MediaLibraryQueryParams> = { page: Number.isSafeInteger(page) && page > 0 ? page : 1 };
  const stringKeys = ['status', 'mediaKind', 'provider', 'sortBy', 'format'] as const;

  for (const key of stringKeys) {
    const value = readOptionalSearchString(search, key);
    if (typeof search[key] === 'string') filters[key] = value ?? '';
  }

  const sortDir = readSortDir(search);
  if (sortDir !== undefined) filters.sortDir = sortDir;
  const query = readOptionalSearchString(search, 'q');
  if (query !== undefined) filters.query = query;
  if (typeof search.collection === 'string' && ['film-tv', 'anime', 'manga', 'books'].includes(search.collection)) {
    filters.collection = search.collection;
  }
  return normalizeCollectionFilters(filters);
}

export function mediaLibraryFilterSearch(filters: MediaLibraryQueryParams) {
  return {
    q: filters.query || undefined,
    collection: filters.collection,
    format: filters.format || undefined,
    status: filters.status ?? '',
    mediaKind: filters.mediaKind ?? '',
    provider: filters.provider ?? '',
    providerListName: filters.providerListName || undefined,
    sortBy: filters.sortBy ?? 'updatedAt',
    sortDir: filters.sortDir ?? 'desc',
    page: filters.page && filters.page > 1 ? filters.page : undefined,
  };
}
