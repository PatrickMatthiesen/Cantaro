export interface WatchProgressObservation {
  schemaVersion: 1;
  provider: 'crunchyroll';
  providerEpisodeId: string;
  providerSeriesId?: string;
  providerSeasonId?: string;
  providerSequenceNumber?: number;
  observedUrl: string;
  seriesTitle: string;
  episodeTitle: string;
  episodeNumber?: number;
  releaseTrack?: string;
  seasonTitle?: string;
  seasonNumber?: number;
  watchProgressPercent: number;
  durationSeconds: number;
  positionSeconds: number;
  nextEpisodeProviderId?: string;
  nextEpisodeUrl?: string;
  nextEpisodeTitle?: string;
  nextEpisodeNumber?: number;
  nextEpisodeReleaseTrack?: string;
  observedAt: string;
  extensionVersion: string;
}

export interface WatchCandidate {
  candidateId: string;
  candidateSource: string;
  mediaTitleId: string;
  provider?: string;
  providerMediaId?: string;
  title: string;
  mediaKind: string;
  score: number;
  explanation?: string;
}

export interface WatchProviderChoice {
  providerId: string;
  providerMediaId: string;
  title: string;
  mediaKind: string;
  isInLibrary: boolean;
  libraryEntryId?: string;
  mediaTitleId?: string;
}

export interface WatchResolution {
  observationId: string;
  observedTitle: string;
  matchStatus: string;
  suggestedEpisodeOffset: number;
  candidates: WatchCandidate[];
  providerChoices: WatchProviderChoice[];
  providerChoicesUnavailableReason?: string;
}

export interface WatchSubmissionResult {
  status: 'accepted' | 'pending_resolution' | 'deduplicated' | 'queued';
  observationId?: string;
  matchedMediaTitleId?: string;
  matchedTitle?: string;
  resolvedProgress?: number;
  progressUpdated?: boolean;
  resolution?: WatchResolution;
}

export interface ResolveWatchObservationRequest {
  observationId: string;
  candidateId?: string;
  providerId?: string;
  providerMediaId?: string;
  episodeOffset: number;
  addToLibraryConfirmed?: boolean;
}
