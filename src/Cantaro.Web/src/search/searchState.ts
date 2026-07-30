export const searchMaxQueryLength = 200;

export type SearchGroupId = 'all' | 'songs' | 'artists' | 'playlists' | 'media';

const searchGroupIds = new Set<SearchGroupId>(['all', 'songs', 'artists', 'playlists', 'media']);

export interface SearchRouteState {
  q?: string;
  group: SearchGroupId;
}

export function normalizeSearchQuery(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined;
  const query = value.trim().slice(0, searchMaxQueryLength);
  return query || undefined;
}

function normalizeSearchGroup(value: unknown): SearchGroupId {
  return typeof value === 'string' && searchGroupIds.has(value as SearchGroupId)
    ? value as SearchGroupId
    : 'all';
}

export function readSearchRouteState(search: Record<string, unknown>): SearchRouteState {
  return {
    q: normalizeSearchQuery(search.q),
    group: normalizeSearchGroup(search.group),
  };
}

export function createGlobalSearchState(query: string): SearchRouteState {
  return {
    q: normalizeSearchQuery(query),
    group: 'all',
  };
}

export function getSearchResultLimit(group: SearchGroupId): number {
  return group === 'all' ? 6 : 20;
}
