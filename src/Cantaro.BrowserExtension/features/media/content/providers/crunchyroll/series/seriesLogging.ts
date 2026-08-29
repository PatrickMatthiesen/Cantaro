import type { SeriesCatalogObservation } from '../../../../contracts/catalogObservation';

export interface CrunchyrollEpisodeLogEntry {
  episodeNumber: number;
  title?: string;
  providerEpisodeId: string;
  url: string;
  releaseTrack?: string;
}

export interface CrunchyrollSeriesDiscoveryLog {
  providerSeriesId: string;
  providerSeasonId?: string;
  season: string;
  episodeCount: number;
  episodes: CrunchyrollEpisodeLogEntry[];
}

/** Builds the deliberately small, public catalog payload shown in verbose extension logs. */
export function buildSeriesDiscoveryLog(
  observation: SeriesCatalogObservation,
): CrunchyrollSeriesDiscoveryLog {
  return {
    providerSeriesId: observation.providerSeriesId,
    providerSeasonId: observation.providerSeasonId,
    season: observation.seasonTitle,
    episodeCount: observation.episodes.length,
    episodes: observation.episodes.map(episode => ({
      episodeNumber: episode.episodeNumber,
      title: episode.episodeTitle,
      providerEpisodeId: episode.providerEpisodeId,
      url: episode.providerUrl,
      releaseTrack: episode.releaseTrack,
    })),
  };
}
