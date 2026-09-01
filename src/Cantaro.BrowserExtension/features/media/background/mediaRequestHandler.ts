import { ApiError } from '../../../platform/api/apiError';
import { backgroundCantaroApiClient } from '../../../platform/api/backgroundCantaroApiClient';
import type { CantaroApiClient } from '../../../platform/api/cantaroApiClient';
import { createExtensionLogger, type ExtensionLogger } from '../../../platform/diagnostics/logger';
import {
  messageFailure,
  messageSuccess,
  type MessageResult,
} from '../../../platform/messaging/messageResult';
import type { MediaBackgroundRequest, MediaBackgroundResponse } from '../contracts/mediaMessages';
import { browserMediaProgressNotifier, type MediaProgressNotifier } from './mediaProgressNotifier';

type SubmissionRequest = Exclude<MediaBackgroundRequest, { type: 'media.watch.resolve' }>;
type ResolutionRequest = Extract<MediaBackgroundRequest, { type: 'media.watch.resolve' }>;

export interface MediaRequestHandler {
  handle(request: MediaBackgroundRequest, tabId?: number): Promise<MessageResult<MediaBackgroundResponse>>;
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
  progressNotifier: MediaProgressNotifier,
): Promise<MessageResult<MediaBackgroundResponse>> {
  try {
    if (request.type === 'media.catalog.submit') {
      const result = await apiClient.submitCatalog(request.payload);
      logSubmission(logger, request, result);
      return messageSuccess(result, request.correlationId);
    }

    const result = await apiClient.submitWatch(request.payload);
    await progressNotifier.notify(result).catch((error: unknown) => {
      logger.warn('Could not notify open Cantaro tabs', { reason: failureMessage(error) });
    });
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
  progressNotifier: MediaProgressNotifier = { notify: async () => {} },
): MediaRequestHandler {
  return {
    async handle(request, tabId) {
      const requestLogger = logger.child({ tabId, correlationId: request.correlationId });
      return request.type === 'media.watch.resolve'
        ? handleResolution(request, apiClient, requestLogger)
        : handleSubmission(request, apiClient, requestLogger, progressNotifier);
    },
  };
}

export const mediaRequestHandler = createMediaRequestHandler(
  backgroundCantaroApiClient,
  createExtensionLogger({ scope: 'background', feature: 'media' }),
  browserMediaProgressNotifier,
);
