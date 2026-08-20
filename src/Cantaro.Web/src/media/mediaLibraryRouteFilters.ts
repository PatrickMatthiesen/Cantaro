import type { MediaLibraryQueryParams } from '@cantaro/client-shared/media';

function readOptionalSearchString(search: Record<string, unknown>, key: string) {
  return typeof search[key] === 'string' && search[key] ? search[key] as string : undefined;
}

function readSortDir(search: Record<string, unknown>) {
  return search.sortDir === 'asc' || search.sortDir === 'desc' ? search.sortDir : undefined;
}

function hasMediaFilterDefaults(search: Record<string, unknown>) {
  return ['status', 'mediaKind', 'provider', 'sortBy', 'sortDir'].some((key) => key in search);
}

export function getMediaFilterDefaults(search: Record<string, unknown>): Partial<MediaLibraryQueryParams> | undefined {
  if (!hasMediaFilterDefaults(search)) return undefined;

  const filters: Partial<MediaLibraryQueryParams> = { page: 1 };
  const stringKeys = ['status', 'mediaKind', 'provider', 'sortBy'] as const;

  for (const key of stringKeys) {
    const value = readOptionalSearchString(search, key);
    if (value !== undefined) filters[key] = value;
  }

  const sortDir = readSortDir(search);
  if (sortDir !== undefined) filters.sortDir = sortDir;

  return filters;
}
