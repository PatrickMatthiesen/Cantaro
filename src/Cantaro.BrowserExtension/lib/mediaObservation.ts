/** Extension version — kept in sync with package.json. */
export const EXTENSION_VERSION = '0.1.0';

/** Site identifiers for each supported observation adapter. */
export const SiteIds = {
  Crunchyroll: 'crunchyroll',
} as const;

export type SiteId = (typeof SiteIds)[keyof typeof SiteIds];

/**
 * Structured observation emitted by a content-script adapter.
 * The backend owns all write decisions; the extension only observes and
 * forwards this payload — it never directly mutates provider progress.
 */
export interface MediaObservation {
  /** Stable site identifier, e.g. "crunchyroll". */
  siteId: SiteId;
  /** Full URL of the observed page at the time of observation. */
  observedUrl: string;
  /** Stable site-specific media identifier extracted from the URL or DOM, if available. */
  siteMediaId?: string;
  /** Human-readable title text of the observed content (series + episode if available). */
  titleText: string;
  /** Series title extracted from the media page when available. */
  seriesTitle?: string;
  /** Episode title extracted from the media page when available. */
  episodeTitle?: string;
  /** Episode number extracted from the media page when available. */
  episodeNumber?: number;
  /** Season title or label extracted from the media page when available. */
  seasonTitle?: string;
  /** Season number extracted from the media page when available. */
  seasonNumber?: number;
  /** Progress hint extracted from the page, e.g. episode number. null when not determinable. */
  progressHint?: number | null;
  /** Watched percentage in the range 0-100 at the time the observation was emitted. */
  watchProgressPercent?: number;
  /** Total video duration in seconds at the time the observation was emitted. */
  durationSeconds?: number;
  /** Current playback position in seconds at the time the observation was emitted. */
  positionSeconds?: number;
  /** ISO 8601 timestamp when the observation was captured. */
  observedAt: string;
  /** Extension version string that produced this observation. */
  extensionVersion: string;
}

/** Backend DTO posted to Cantaro's media observations API. */
export interface SubmitMediaObservationRequest {
  siteIdentifier: SiteId;
  observedUrl: string;
  siteMediaId?: string;
  observedTitle: string;
  seriesTitle?: string;
  episodeTitle?: string;
  episodeNumber?: number;
  seasonTitle?: string;
  seasonNumber?: number;
  progressHint?: string;
  watchProgressPercent?: number;
  durationSeconds?: number;
  positionSeconds?: number;
  observedAt: string;
  extensionVersion: string;
}

export interface MediaObservationCandidateDto {
  candidateId: string;
  candidateSource: string;
  mediaTitleId: string;
  provider?: string;
  providerMediaId?: string;
  title: string;
  mediaKind: string;
  score: number;
  explanation?: string;
  isAccepted: boolean;
}

export interface MediaObservationProviderChoiceDto {
  providerId: string;
  providerMediaId: string;
  title: string;
  nativeTitle?: string;
  mediaKind: string;
  posterUrl?: string;
  backgroundUrl?: string;
  startYear?: number;
  episodeCount?: number;
  chapterCount?: number;
  volumeCount?: number;
  primaryProgressDimension: string;
  isInLibrary: boolean;
  libraryEntryId?: string;
  mediaTitleId?: string;
}

export interface MediaObservationDto {
  observationId: string;
  siteIdentifier: string;
  observedUrl: string;
  siteMediaId?: string;
  observedTitle: string;
  progressHint?: string;
  observedAt: string;
  extensionVersion?: string;
  matchStatus: string;
  mediaTitleId?: string;
  resolutionNotes?: string;
  observedProgress?: number;
  episodeOffset?: number;
  resolvedProgress?: number;
  resolvedLibraryEntryId?: string;
  candidates: MediaObservationCandidateDto[];
  providerChoices: MediaObservationProviderChoiceDto[];
}

export interface SubmitMediaObservationResponse {
  observationId: string;
  matchStatus: string;
  wasDeduplicated: boolean;
  matchedMediaTitleId?: string;
  matchedTitle?: string;
  candidateCount: number;
  requiresResolution: boolean;
  observedProgress?: number;
  suggestedEpisodeOffset: number;
  resolvedProgress?: number;
  observation?: MediaObservationDto;
  providerChoices: MediaObservationProviderChoiceDto[];
  providerChoicesUnavailableReason?: string;
}

export interface ResolveMediaObservationRequest {
  candidateId?: string;
  providerId?: string;
  providerMediaId?: string;
  episodeOffset: number;
  addToLibraryConfirmed?: boolean;
}

export function toSubmitMediaObservationRequest(
  observation: MediaObservation,
): SubmitMediaObservationRequest {
  return {
    siteIdentifier: observation.siteId,
    observedUrl: observation.observedUrl,
    siteMediaId: observation.siteMediaId,
    observedTitle: observation.titleText,
    seriesTitle: observation.seriesTitle,
    episodeTitle: observation.episodeTitle,
    episodeNumber: observation.episodeNumber,
    seasonTitle: observation.seasonTitle,
    seasonNumber: observation.seasonNumber,
    progressHint:
      observation.progressHint === null || observation.progressHint === undefined
        ? undefined
        : String(observation.progressHint),
    watchProgressPercent: observation.watchProgressPercent,
    durationSeconds: observation.durationSeconds,
    positionSeconds: observation.positionSeconds,
    observedAt: observation.observedAt,
    extensionVersion: observation.extensionVersion,
  };
}

/** Internal message envelope sent from content scripts to the background worker. */
export type MediaObservationMessage =
  | {
    type: 'MEDIA_OBSERVATION';
    payload: MediaObservation;
  }
  | {
    type: 'SHOW_MEDIA_RESOLUTION';
    payload: SubmitMediaObservationResponse;
  }
  | {
    type: 'GET_LATEST_MEDIA_RESOLUTION';
  }
  | {
    type: 'RESOLVE_MEDIA_OBSERVATION';
    payload: {
      observationId: string;
      request: ResolveMediaObservationRequest;
    };
  }
  | {
    type: 'DRAIN_MEDIA_OBSERVATION_QUEUE';
  };
