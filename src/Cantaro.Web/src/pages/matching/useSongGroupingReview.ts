import { useCallback, useEffect, useState } from 'react';
import { matchingApi } from '@cantaro/client-shared/music';
import type { SongGroupingSuggestionPageResponse } from '@cantaro/client-shared/music';

export interface SongGroupingReviewState {
  activeSuggestionId: string | null;
  error: string | null;
  isGenerating: boolean;
  isLoading: boolean;
  loadSuggestions: () => Promise<void>;
  pageData: SongGroupingSuggestionPageResponse | null;
  reviewSuggestion: (suggestionId: string, accept: boolean) => Promise<void>;
  runGeneration: () => Promise<void>;
  setPage: (page: number) => void;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

export function useSongGroupingReview(): SongGroupingReviewState {
  const [page, setPage] = useState(1);
  const [pageData, setPageData] = useState<SongGroupingSuggestionPageResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isGenerating, setIsGenerating] = useState(false);
  const [activeSuggestionId, setActiveSuggestionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const loadSuggestions = useCallback(async () => {
    try {
      const response = await matchingApi.getSongGroupingSuggestions(page);
      setPageData(response);
      if (response.page !== page) setPage(response.page);
      setError(null);
    } catch (loadError) {
      setError(errorMessage(loadError, 'Failed to load song grouping suggestions'));
    } finally {
      setIsLoading(false);
    }
  }, [page]);
  useEffect(() => { void loadSuggestions(); }, [loadSuggestions]);
  const runGeneration = useCallback(async () => {
    setIsGenerating(true);
    try {
      await matchingApi.generateSongGroupingSuggestions();
      await loadSuggestions();
    } catch (generationError) {
      setError(errorMessage(generationError, 'Failed to find song grouping candidates'));
    } finally {
      setIsGenerating(false);
    }
  }, [loadSuggestions]);
  const reviewSuggestion = useCallback(async (suggestionId: string, accept: boolean) => {
    setActiveSuggestionId(suggestionId);
    try {
      await matchingApi.reviewSongGroupingSuggestion(suggestionId, accept);
      await loadSuggestions();
    } catch (reviewError) {
      setError(errorMessage(reviewError, 'Song grouping review failed'));
    } finally {
      setActiveSuggestionId(null);
    }
  }, [loadSuggestions]);
  return { activeSuggestionId, error, isGenerating, isLoading, loadSuggestions, pageData, reviewSuggestion, runGeneration, setPage };
}
