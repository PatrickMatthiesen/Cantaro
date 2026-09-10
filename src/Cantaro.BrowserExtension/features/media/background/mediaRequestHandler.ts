import { ApiError } from '../../../platform/api/apiError';
import { backgroundCantaroApiClient } from '../../../platform/api/backgroundCantaroApiClient';
import type { CantaroApiClient } from '../../../platform/api/cantaroApiClient';
import { createExtensionLogger, type ExtensionLogger } from '../../../platform/diagnostics/logger';
import { browserConsentService, type ConsentStatus } from '../../../platform/consent/consentService';
import { browserSettingsRepository, type SettingsRepository } from '../../../platform/settings/settingsRepository';
import {
  messageFailure,
  messageSuccess,
  type MessageResult,
} from '../../../platform/messaging/messageResult';
import type { MediaBackgroundRequest, MediaBackgroundResponse } from '../contracts/mediaMessages';

type SubmissionRequest = Exclude<MediaBackgroundRequest, { type: 'media.watch.resolve' }>;
type ResolutionRequest = Extract<MediaBackgroundRequest, { type: 'media.watch.resolve' }>;

export interface MediaRequestHandler {
  handle(request: MediaBackgroundRequest, tabId?: number): Promise<MessageResult<MediaBackgroundResponse>>;
}

export interface MediaConsentGate {
  getStatus(baseUrl: string): Promise<ConsentStatus>;
}

function removeCatalogEvidence(
  request: Extract<MediaBackgroundRequest, { type: 'media.watch.submit' }>,
): Extract<MediaBackgroundRequest, { type: 'media.watch.submit' }> {
  return {
    ...request,
    payload: {
      ...request.payload,
      nextEpisodeProviderId: undefined,
      nextEpisodeUrl: undefined,
      nextEpisodeTitle: undefined,
      nextEpisodeNumber: undefined,
      nextEpisodeReleaseTrack: undefined,
    },
  };
}

function failureMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'The delivery failed unexpectedly.';
}

function failureResult(
  request: MediaBackgroundRequest,
  error: unknown,
): MessageResult<never> {
  const apiError = error instanceof ApiError ? error : null;
  const code = apiError?.status === 401
    ? 'not_authenticated'
    : apiError ? 'api_rejected' : 'unexpected';
  return messageFailure(
    request.correlationId,
    code,
    failureMessage(error),
    apiError?.retryable ?? false,
  );
}

function logSubmission(
  logger: ExtensionLogger,
  request: SubmissionRequest,
  result: MediaBackgroundResponse,
): void {
  const identity = request.type === 'media.catalog.submit'
    ? {
      providerSeriesId: request.payload.providerSeriesId,
      episodeCount: request.payload.episodes.length,
    }
    : { providerEpisodeId: request.payload.providerEpisodeId };
  logger.info('Media observation delivered', { ...identity, status: 'status' in result ? result.status : undefined });
}

async function handleSubmission(
  request: SubmissionRequest,
  apiClient: CantaroApiClient,
  logger: ExtensionLogger,
): Promise<MessageResult<MediaBackgroundResponse>> {
  try {
    if (request.type === 'media.catalog.submit') {
      const result = await apiClient.submitCatalog(request.payload);
      logSubmission(logger, request, result);
      return messageSuccess(result, request.correlationId);
    }

    const result = await apiClient.submitWatch(request.payload);
    logSubmission(logger, request, result);
    return messageSuccess(result, request.correlationId);
  } catch (error) {
    logger.error('Media request failed', error);
    return failureResult(request, error);
  }
}

async function handleResolution(
  request: ResolutionRequest,
  apiClient: CantaroApiClient,
  logger: ExtensionLogger,
): Promise<MessageResult<MediaBackgroundResponse>> {
  try {
    await apiClient.resolveWatch(request.payload);
    logger.info('Watch observation resolved', {
      observationId: request.payload.observationId,
    });
    return messageSuccess({ resolved: true }, request.correlationId);
  } catch (error) {
    logger.error('Media request failed', error);
    return failureResult(request, error);
  }
}

export function createMediaRequestHandler(
  apiClient: CantaroApiClient,
  logger: ExtensionLogger,
  consentGate: MediaConsentGate = browserConsentService,
  settingsRepository: SettingsRepository = browserSettingsRepository,
): MediaRequestHandler {
  return {
    async handle(request, tabId) {
      const requestLogger = logger.child({ tabId, correlationId: request.correlationId });
      try {
        const settings = await settingsRepository.read();
        const consent = await consentGate.getStatus(settings.baseUrl);
        const permitted = consent.authenticated && (request.type === 'media.catalog.submit'
          ? consent.catalogCollectionAllowed
          : consent.watchTrackingAllowed);
        if (!permitted) {
          return messageFailure(
            request.correlationId,
            consent.authenticated ? 'consent_required' : 'not_authenticated',
            consent.authenticated
              ? 'Enable the corresponding Cantaro collection option before submitting website observations.'
              : 'Sign in and enable Cantaro collection before submitting website observations.',
          );
        }
        const effectiveRequest = request.type === 'media.watch.submit'
          && !consent.catalogCollectionAllowed
          ? removeCatalogEvidence(request)
          : request;
        return effectiveRequest.type === 'media.watch.resolve'
          ? handleResolution(effectiveRequest, apiClient, requestLogger)
          : handleSubmission(effectiveRequest, apiClient, requestLogger);
      } catch (error) {
        requestLogger.error('Media consent check failed', error);
        return failureResult(request, error);
      }
    },
  };
}

export const mediaRequestHandler = createMediaRequestHandler(
  backgroundCantaroApiClient,
  createExtensionLogger({ scope: 'background', feature: 'media' }),
);
