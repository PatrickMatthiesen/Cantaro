import type { CatalogSubmissionStatus } from './catalogObservation';

export type ContentControllerStatus =
  | 'starting'
  | 'ready'
  | 'submitting'
  | 'submitted'
  | 'queued'
  | 'paused'
  | 'error';

export interface MediaSeriesTabContext {
  feature: 'media';
  provider: 'crunchyroll';
  pageKind: 'series';
  pageUrl: string;
  status: ContentControllerStatus;
  providerSeriesId?: string;
  seriesTitle?: string;
  seasonTitle?: string;
  observedEpisodeCount: number;
  submissionStatus?: CatalogSubmissionStatus;
  message?: string;
}

export interface MediaWatchTabContext {
  feature: 'media';
  provider: 'crunchyroll';
  pageKind: 'watch';
  pageUrl: string;
  status: ContentControllerStatus;
  providerEpisodeId?: string;
  seriesTitle?: string;
  episodeTitle?: string;
  episodeNumber?: number;
  watchProgressPercent?: number;
  message?: string;
}

export type MediaTabContext = MediaSeriesTabContext | MediaWatchTabContext;
