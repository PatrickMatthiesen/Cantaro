import type { MediaContinueWatchingDto } from '../../services/mediaApi';
import type { ContinueWatchingState } from './mediaEntryDetailTypes';

export interface ContinueLinkAction {
  url: string;
  label: string;
  isEpisodeLink: boolean;
}

const MAX_CRUNCHYROLL_SEARCH_QUERY_CHARACTERS = 32;

function getCrunchyrollSearchUrl(title: string): string {
  const query = Array.from(title.trim())
    .slice(0, MAX_CRUNCHYROLL_SEARCH_QUERY_CHARACTERS)
    .join('')
    .trimEnd();
  return `https://www.crunchyroll.com/search?q=${encodeURIComponent(query)}`;
}

function getDirectEpisodeAction(value: MediaContinueWatchingDto): ContinueLinkAction | null {
  if (value.outcome !== 'direct' || !value.url) return null;
  return {
    url: value.url,
    label: `Continue episode ${value.episodeNumber ?? ''}`.trim(),
    isEpisodeLink: true,
  };
}

function getSeriesUrl(value: MediaContinueWatchingDto, knownSeriesUrl: string | null) {
  return value.outcome === 'series_fallback' ? value.url ?? knownSeriesUrl : knownSeriesUrl;
}

function getFallbackAction(
  value: MediaContinueWatchingDto,
  knownSeriesUrl: string | null,
  canonicalTitle: string,
): ContinueLinkAction {
  const seriesUrl = getSeriesUrl(value, knownSeriesUrl);
  if (seriesUrl) {
    return { url: seriesUrl, label: 'Open series on Crunchyroll', isEpisodeLink: false };
  }
  return {
    url: getCrunchyrollSearchUrl(canonicalTitle),
    label: 'Search Crunchyroll',
    isEpisodeLink: false,
  };
}

export function getContinueLinkAction(
  state: ContinueWatchingState,
  knownSeriesUrl: string | null,
  canonicalTitle: string,
): ContinueLinkAction | null {
  if (state.status !== 'loaded' || state.value.outcome === 'completed') return null;
  return getDirectEpisodeAction(state.value)
    ?? getFallbackAction(state.value, knownSeriesUrl, canonicalTitle);
}
