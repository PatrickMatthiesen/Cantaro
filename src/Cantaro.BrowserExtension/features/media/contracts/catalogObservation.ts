// Defensive transport/DOM-corruption ceiling. The API decides how many episodes are valid
// for a matched title; this must remain high enough for providers that group long runs together.
export const MAX_CATALOG_EPISODES_PER_OBSERVATION = 2_000;

export interface CatalogEpisodeObservation {
  providerEpisodeId: string;
  providerUrl: string;
  episodeNumber: number;
  episodeTitle?: string;
  releaseTrack?: string;
  availableSubtitleLanguageCodes?: string[];
  availableAudioLanguageCodes?: string[];
}

export interface SeriesCatalogObservation {
  schemaVersion: 1;
  provider: 'crunchyroll';
  providerSeriesId: string;
  providerSeasonId?: string;
  seriesTitle: string;
  seasonTitle: string;
  seasonNumber?: number;
  seriesUrl: string;
  episodes: CatalogEpisodeObservation[];
  observedAt: string;
  extensionVersion: string;
}

export type CatalogSubmissionStatus =
  | 'accepted'
  | 'pending_match'
  | 'deduplicated'
  | 'queued'
  | 'rejected';

export interface CatalogSubmissionResult {
  status: CatalogSubmissionStatus;
  observationId?: string;
  matchedMediaTitleId?: string;
  acceptedEpisodeCount: number;
  reason?: string;
}
