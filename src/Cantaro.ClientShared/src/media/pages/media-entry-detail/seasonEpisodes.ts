import type { EpisodeStreamingDestinations } from '../../services/streamingDestinations';

export type SeasonSelection = 'all' | 'specials' | number;

export interface SeasonOption {
  value: SeasonSelection;
  label: string;
  episodeCount: number;
}

export interface SelectedSeasonProgress {
  seasonNumber: number;
  value: number;
  total: number;
  overallValue: number;
  canEdit: boolean;
}

type EpisodeNumbering = Pick<
  EpisodeStreamingDestinations,
  'episodeNumber' | 'seasonNumber' | 'seasonEpisodeNumber'
>;

function hasRegularSeasonMapping(episode: EpisodeNumbering) {
  return Number.isInteger(episode.seasonNumber)
    && (episode.seasonNumber ?? 0) > 0
    && Number.isInteger(episode.seasonEpisodeNumber)
    && (episode.seasonEpisodeNumber ?? 0) > 0;
}

function getMappedRegularEpisodes(episodes: readonly EpisodeStreamingDestinations[]) {
  return episodes
    .filter(hasRegularSeasonMapping)
    .sort((left, right) => left.episodeNumber - right.episodeNumber);
}

export function getSeasonOptions(
  episodes: readonly EpisodeStreamingDestinations[],
  specialCount = 0,
): SeasonOption[] {
  const seasonCounts = new Map<number, number>();
  for (const episode of getMappedRegularEpisodes(episodes)) {
    const seasonNumber = episode.seasonNumber as number;
    seasonCounts.set(seasonNumber, (seasonCounts.get(seasonNumber) ?? 0) + 1);
  }

  const options: SeasonOption[] = [
    { value: 'all', label: 'All seasons', episodeCount: episodes.length },
    ...[...seasonCounts.entries()]
      .sort(([left], [right]) => left - right)
      .map(([seasonNumber, episodeCount]) => ({
        value: seasonNumber,
        label: `Season ${seasonNumber}`,
        episodeCount,
      })),
  ];

  if (specialCount > 0) {
    options.push({ value: 'specials', label: 'Specials', episodeCount: specialCount });
  }
  return options;
}

export function getDefaultSeasonSelection(
  episodes: readonly EpisodeStreamingDestinations[],
  overallProgress: number,
): SeasonSelection {
  const mapped = getMappedRegularEpisodes(episodes);
  const nextEpisode = mapped.find((episode) => episode.episodeNumber > overallProgress);
  if (nextEpisode?.seasonNumber) return nextEpisode.seasonNumber;

  const lastRegularEpisode = mapped.at(-1);
  return lastRegularEpisode?.seasonNumber ?? 'all';
}

export function isSeasonSelectionAvailable(
  selection: SeasonSelection,
  options: readonly SeasonOption[],
) {
  return options.some((option) => option.value === selection);
}

export function getEpisodesForSeason(
  episodes: readonly EpisodeStreamingDestinations[],
  selection: SeasonSelection,
) {
  if (selection === 'all') return [...episodes];
  if (selection === 'specials') return [];
  return episodes.filter((episode) =>
    hasRegularSeasonMapping(episode) && episode.seasonNumber === selection);
}

function getSelectedSeasonEpisodes(
  episodes: readonly EpisodeStreamingDestinations[],
  seasonNumber: number,
) {
  return getEpisodesForSeason(episodes, seasonNumber)
    .sort((left, right) =>
      (left.seasonEpisodeNumber as number) - (right.seasonEpisodeNumber as number));
}

function hasSafeProgressMapping(episodes: readonly EpisodeStreamingDestinations[]) {
  return episodes.length > 0 && episodes.every((episode, index) => {
    const previous = episodes[index - 1];
    return episode.seasonEpisodeNumber === index + 1
      && (!previous || episode.episodeNumber === previous.episodeNumber + 1);
  });
}

export function getSelectedSeasonProgress(
  episodes: readonly EpisodeStreamingDestinations[],
  selection: SeasonSelection,
  overallProgress: number,
): SelectedSeasonProgress | null {
  if (typeof selection !== 'number') return null;
  const seasonEpisodes = getSelectedSeasonEpisodes(episodes, selection);
  if (seasonEpisodes.length === 0) return null;

  const watchedEpisodes = seasonEpisodes.filter(
    (episode) => episode.episodeNumber <= overallProgress,
  );
  const watchedEpisode = watchedEpisodes.at(-1);
  const lastEpisode = seasonEpisodes[seasonEpisodes.length - 1]!;
  return {
    seasonNumber: selection,
    value: watchedEpisode ? watchedEpisode.seasonEpisodeNumber as number : 0,
    total: lastEpisode.seasonEpisodeNumber as number,
    overallValue: overallProgress,
    canEdit: hasSafeProgressMapping(seasonEpisodes),
  };
}

export function mapSeasonProgressToOverall(
  episodes: readonly EpisodeStreamingDestinations[],
  seasonNumber: number,
  seasonEpisodeNumber: number,
): number | null {
  const seasonEpisodes = getSelectedSeasonEpisodes(episodes, seasonNumber);
  if (!hasSafeProgressMapping(seasonEpisodes)) return null;
  if (seasonEpisodeNumber === 0) {
    return seasonEpisodes[0]!.episodeNumber - 1;
  }
  const selectedEpisode = seasonEpisodes.find(
    (episode) => episode.seasonEpisodeNumber === seasonEpisodeNumber,
  );
  return selectedEpisode ? selectedEpisode.episodeNumber : null;
}

export function formatEpisodeNumber(
  episode: EpisodeNumbering,
  selection: SeasonSelection,
) {
  if (!hasRegularSeasonMapping(episode)) {
    return { primary: `Episode ${episode.episodeNumber}` };
  }

  const relativeNumber = episode.seasonEpisodeNumber as number;
  const seasonNumber = episode.seasonNumber as number;
  return {
    primary: selection === 'all'
      ? `Season ${seasonNumber}, Episode ${relativeNumber}`
      : `Episode ${relativeNumber}`,
    secondary: `Overall episode ${episode.episodeNumber}`,
  };
}
