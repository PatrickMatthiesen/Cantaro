export const searchMaxQueryLength = 200;

export type SearchGroupId = 'all' | 'songs' | 'artists' | 'playlists' | 'media';

const searchGroupIds = new Set<SearchGroupId>(['all', 'songs', 'artists', 'playlists', 'media']);

export interface SearchRouteState {
  q?: string;
  group: SearchGroupId;
  preview?: true;
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
  const preview = search.preview === true || search.preview === 'true';
  return {
    q: normalizeSearchQuery(search.q),
    group: normalizeSearchGroup(search.group),
    ...(preview ? { preview: true as const } : {}),
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

export type SearchSurfaceKind = 'desktop-preview' | 'mobile-preview' | 'mobile-expanded';

export function getSearchSurfaceKind(isMobile: boolean, pathname: string): SearchSurfaceKind {
  if (!isMobile) return 'desktop-preview';
  return pathname === '/search' ? 'mobile-expanded' : 'mobile-preview';
}

export function getMobileSearchCloseAction(
  pathname: string,
  canGoBack: boolean,
): 'back' | 'fallback' | 'hide' {
  if (pathname === '/search' && canGoBack) return 'back';
  if (pathname === '/search') return 'fallback';
  return 'hide';
}
