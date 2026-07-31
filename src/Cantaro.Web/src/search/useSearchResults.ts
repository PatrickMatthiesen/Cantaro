import { useEffect, useRef, useState } from 'react';
import { searchCantaro, type SearchResponse } from './searchApi';
import { normalizeSearchQuery } from './searchState';

interface SearchResultsOptions {
  limitPerGroup: number;
  includeDiscovery: boolean;
  debounceMs?: number;
  enabled?: boolean;
}

export interface SearchResultsState {
  response: SearchResponse | null;
  error: string | null;
  loading: boolean;
  retry: () => void;
}

export function useSearchResults(query: string, options: SearchResultsOptions): SearchResultsState {
  const normalizedQuery = normalizeSearchQuery(query) ?? '';
  const [response, setResponse] = useState<SearchResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [retryKey, setRetryKey] = useState(0);
  const requestGeneration = useRef(0);
  const {
    debounceMs = 0,
    enabled = true,
    includeDiscovery,
    limitPerGroup,
  } = options;

  useEffect(() => {
    const generation = requestGeneration.current + 1;
    requestGeneration.current = generation;

    if (!normalizedQuery || !enabled) {
      setResponse(null);
      setError(null);
      setLoading(false);
      return;
    }

    const abortController = new AbortController();
    setLoading(true);
    setError(null);
    const timeout = window.setTimeout(() => {
      void searchCantaro(normalizedQuery, {
        includeDiscovery,
        limitPerGroup,
        signal: abortController.signal,
      })
        .then((result) => {
          if (requestGeneration.current === generation) setResponse(result);
        })
        .catch((reason: unknown) => {
          if (reason instanceof Error && reason.name === 'AbortError') return;
          if (requestGeneration.current !== generation) return;
          setResponse(null);
          setError(reason instanceof Error ? reason.message : 'Cantaro search is temporarily unavailable.');
        })
        .finally(() => {
          if (!abortController.signal.aborted && requestGeneration.current === generation) setLoading(false);
        });
    }, debounceMs);

    return () => {
      window.clearTimeout(timeout);
      abortController.abort();
    };
  }, [debounceMs, enabled, includeDiscovery, limitPerGroup, normalizedQuery, retryKey]);

  return {
    response,
    error,
    loading,
    retry: () => setRetryKey((key) => key + 1),
  };
}
