import { normalizeSearchQuery, type SearchGroupId } from './searchState';

export type SearchEntityType = 'song' | 'artist' | 'playlist' | 'media';
export type SearchGroupStatus = 'ok' | 'unavailable' | 'failed';

export type { SearchGroupId } from './searchState';
export type SearchResultGroupId = Exclude<SearchGroupId, 'all'>;

export interface SearchResultItem {
  entityType: SearchEntityType;
  id: string;
  title: string;
  subtitle?: string;
  detail?: string;
  artworkUrl?: string;
  canonicalRoute: string;
}

export interface SearchResultGroup {
  status: SearchGroupStatus;
  items: SearchResultItem[];
  hasMore: boolean;
  message?: string;
}

export interface SearchResponse {
  query: string;
  groups: Record<SearchResultGroupId, SearchResultGroup>;
}

function readErrorMessage(value: unknown): string | undefined {
  const body = value as { error?: unknown; title?: unknown } | null;
  const candidate = [body?.error, body?.title].find((item) => typeof item === 'string');
  const message = typeof candidate === 'string' ? candidate.trim() : '';
  return message || undefined;
}

export async function searchCantaro(
  query: string,
  options: { limitPerGroup?: number; signal?: AbortSignal } = {},
): Promise<SearchResponse> {
  const normalizedQuery = normalizeSearchQuery(query);
  if (!normalizedQuery) {
    throw new Error('Enter something to search for.');
  }

  const params = new URLSearchParams({
    q: normalizedQuery,
    limitPerGroup: String(Math.min(Math.max(options.limitPerGroup ?? 6, 1), 20)),
  });
  const response = await fetch(`/api/search?${params.toString()}`, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
    signal: options.signal,
  });

  if (!response.ok) {
    const body: unknown = await response.json().catch(() => null);
    throw new Error(readErrorMessage(body) ?? 'Cantaro search is temporarily unavailable.');
  }

  return response.json() as Promise<SearchResponse>;
}
