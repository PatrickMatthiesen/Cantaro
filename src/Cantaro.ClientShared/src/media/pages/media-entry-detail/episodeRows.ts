import type {
  MediaLibraryEntryDetailDto,
} from '../../services/mediaApi';
import type { EpisodeStreamingDestinations } from '../../services/streamingDestinations';

function getKnownEpisodeCount(episodeCount: number | undefined, knownNumbers: number[]) {
  return episodeCount ?? Math.max(0, ...knownNumbers);
}

function getSparseEpisodeNumbers(
  knownNumbers: number[],
  nextEpisodeNumber: number,
  episodeCount: number,
) {
  const numbers = episodeCount <= 0 || nextEpisodeNumber <= episodeCount
    ? [...knownNumbers, nextEpisodeNumber]
    : knownNumbers;
  return [...new Set(numbers)].sort((left, right) => left - right);
}

function getEpisodeNumbers(entry: MediaLibraryEntryDetailDto, knownNumbers: number[]) {
  const episodeCount = getKnownEpisodeCount(entry.title.episodeCount, knownNumbers);
  if (episodeCount > 0 && episodeCount <= 100) {
    return Array.from({ length: episodeCount }, (_, index) => index + 1);
  }

  return getSparseEpisodeNumbers(
    knownNumbers,
    (entry.progressEpisodes ?? 0) + 1,
    episodeCount,
  );
}

export function getEpisodeRows(
  entry: MediaLibraryEntryDetailDto,
  episodes: EpisodeStreamingDestinations[],
) {
  const knownNumbers = episodes.map((episode) => episode.episodeNumber);
  const episodesByNumber = new Map(episodes.map((episode) => [episode.episodeNumber, episode]));

  return getEpisodeNumbers(entry, knownNumbers).map((episodeNumber) => ({
    episodeNumber,
    destination: episodesByNumber.get(episodeNumber),
  }));
}
