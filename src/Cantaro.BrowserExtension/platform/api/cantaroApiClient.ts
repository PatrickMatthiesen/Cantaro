import type {
  CatalogSubmissionResult,
  SeriesCatalogObservation,
} from '../../features/media/contracts/catalogObservation';
import type {
  ResolveWatchObservationRequest,
  WatchProgressObservation,
  WatchResolution,
  WatchSubmissionResult,
} from '../../features/media/contracts/watchObservation';
import { runtimeAccessTokenProvider } from '../auth/runtimeAuthClient';
import { browserSettingsRepository, type SettingsRepository } from '../settings/settingsRepository';
import { ApiError, apiErrorMessage } from './apiError';

interface CatalogApiResponse {
  status: Exclude<CatalogSubmissionResult['status'], 'queued'>;
  observationId?: string;
  matchedMediaTitleId?: string;
  recordedEpisodeCount: number;
  error?: string;
}

interface WatchApiResponse {
  observationId: string;
  matchStatus: string;
  wasDeduplicated: boolean;
  matchedMediaTitleId?: string;
  matchedTitle?: string;
  resolvedProgress?: number;
  progressUpdated: boolean;
  requiresResolution: boolean;
  suggestedEpisodeOffset: number;
  observation?: {
    observedTitle: string;
    candidates: WatchResolution['candidates'];
  };
  providerChoices: WatchResolution['providerChoices'];
  providerChoicesUnavailableReason?: string;
}

export interface CantaroApiClient {
  request<T>(path: string, init?: RequestInit): Promise<T>;
  submitCatalog(observation: SeriesCatalogObservation): Promise<CatalogSubmissionResult>;
  submitWatch(observation: WatchProgressObservation): Promise<WatchSubmissionResult>;
  resolveWatch(request: ResolveWatchObservationRequest): Promise<void>;
}

export interface AccessTokenProvider {
  getAccessToken(baseUrl: string, forceRefresh?: boolean): Promise<string | null>;
}

async function responsePayload(response: Response): Promise<unknown> {
  if (response.status === 204) return null;
  return response.json().catch(() => null);
}

function watchRequest(observation: WatchProgressObservation): Record<string, unknown> {
  return {
    siteIdentifier: observation.provider,
    observedUrl: observation.observedUrl,
    siteMediaId: observation.providerEpisodeId,
    observedTitle: observation.episodeTitle,
    seriesTitle: observation.seriesTitle,
    episodeTitle: observation.episodeTitle,
    episodeNumber: observation.episodeNumber,
    seasonTitle: observation.seasonTitle,
    seasonNumber: observation.seasonNumber,
    providerSeriesId: observation.providerSeriesId,
    providerSeasonId: observation.providerSeasonId,
    providerSequenceNumber: observation.providerSequenceNumber,
    releaseTrack: observation.releaseTrack,
    nextEpisodeProviderId: observation.nextEpisodeProviderId,
    nextEpisodeUrl: observation.nextEpisodeUrl,
    nextEpisodeTitle: observation.nextEpisodeTitle,
    nextEpisodeNumber: observation.nextEpisodeNumber,
    nextEpisodeReleaseTrack: observation.nextEpisodeReleaseTrack,
    progressHint: observation.episodeNumber?.toString(),
    watchProgressPercent: observation.watchProgressPercent,
    durationSeconds: observation.durationSeconds,
    positionSeconds: observation.positionSeconds,
    observedAt: observation.observedAt,
    extensionVersion: observation.extensionVersion,
  };
}

function toWatchResult(response: WatchApiResponse): WatchSubmissionResult {
  const resolution = response.requiresResolution ? {
    observationId: response.observationId,
    observedTitle: response.observation?.observedTitle ?? response.matchedTitle ?? '',
    matchStatus: response.matchStatus,
    suggestedEpisodeOffset: response.suggestedEpisodeOffset,
    candidates: response.observation?.candidates ?? [],
    providerChoices: response.providerChoices ?? [],
    providerChoicesUnavailableReason: response.providerChoicesUnavailableReason,
  } : undefined;
  return {
    status: response.wasDeduplicated
      ? 'deduplicated'
      : response.requiresResolution ? 'pending_resolution' : 'accepted',
    observationId: response.observationId,
    matchedMediaTitleId: response.matchedMediaTitleId,
    matchedTitle: response.matchedTitle,
    resolvedProgress: response.resolvedProgress,
    progressUpdated: response.progressUpdated,
    resolution,
  };
}

export function createCantaroApiClient(
  settingsRepository: SettingsRepository,
  authService: AccessTokenProvider,
): CantaroApiClient {
  async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const settings = await settingsRepository.read();
    let token = await authService.getAccessToken(settings.baseUrl);
    if (!token) throw new ApiError('Sign in to Cantaro first.', 401, false);

    const send = (accessToken: string) => fetch(`${settings.baseUrl}${path}`, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...init.headers,
        Authorization: `Bearer ${accessToken}`,
      },
    });
    let response: Response;
    try {
      response = await send(token);
    } catch {
      throw new ApiError('Cantaro API could not be reached.', null, true);
    }
    if (response.status === 401) {
      token = await authService.getAccessToken(settings.baseUrl, true);
      if (token) {
        try {
          response = await send(token);
        } catch {
          throw new ApiError('Cantaro API could not be reached.', null, true);
        }
      }
    }
    const payload = await responsePayload(response);
    if (!response.ok) {
      throw new ApiError(
        apiErrorMessage(payload, `Cantaro API request failed (${response.status}).`),
        response.status,
        response.status >= 500 || response.status === 408 || response.status === 429,
      );
    }
    return payload as T;
  }

  return {
    request,

    async submitCatalog(observation) {
      const response = await request<CatalogApiResponse>('/api/media/catalog-observations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(observation),
      });
      return {
        status: response.status,
        observationId: response.observationId,
        matchedMediaTitleId: response.matchedMediaTitleId,
        acceptedEpisodeCount: response.recordedEpisodeCount,
        reason: response.error,
      };
    },

    async submitWatch(observation) {
      const response = await request<WatchApiResponse>('/api/media/observations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(watchRequest(observation)),
      });
      return toWatchResult(response);
    },

    async resolveWatch(resolveRequest) {
      const { observationId, ...body } = resolveRequest;
      await request(`/api/media/observations/${encodeURIComponent(observationId)}/resolve`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
    },
  };
}

export const cantaroApiClient = createCantaroApiClient(
  browserSettingsRepository,
  runtimeAccessTokenProvider,
);
