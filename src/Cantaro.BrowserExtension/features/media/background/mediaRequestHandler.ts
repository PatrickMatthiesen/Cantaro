import { ApiError } from '../../../platform/api/apiError';
import { backgroundCantaroApiClient } from '../../../platform/api/backgroundCantaroApiClient';
import type { CantaroApiClient } from '../../../platform/api/cantaroApiClient';
import { createExtensionLogger, type ExtensionLogger } from '../../../platform/diagnostics/logger';
import {
  messageFailure,
  messageSuccess,
  type MessageResult,
} from '../../../platform/messaging/messageResult';
import {
  browserDeliveryQueue,
  type DeliveryQueue,
  type QueuedDelivery,
} from '../../../platform/storage/deliveryQueue';
import type { DrainDeliveryQueueResult } from '../../../platform/messaging/platformMessages';
import type { MediaBackgroundRequest, MediaBackgroundResponse } from '../contracts/mediaMessages';

type SubmissionRequest = Exclude<MediaBackgroundRequest, { type: 'media.watch.resolve' }>;
type ResolutionRequest = Extract<MediaBackgroundRequest, { type: 'media.watch.resolve' }>;

export interface MediaRequestHandler {
  handle(request: MediaBackgroundRequest, tabId?: number): Promise<MessageResult<MediaBackgroundResponse>>;
  drainQueue(): Promise<DrainDeliveryQueueResult>;
}

function queueableError(error: unknown): boolean {
  return error instanceof ApiError && (error.retryable || error.status === 401);
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

async function submitObservation(
  apiClient: CantaroApiClient,
  request: SubmissionRequest,
): Promise<MediaBackgroundResponse> {
  return request.type === 'media.catalog.submit'
    ? apiClient.submitCatalog(request.payload)
    : apiClient.submitWatch(request.payload);
}

function queuedDelivery(request: SubmissionRequest): QueuedDelivery {
  return request.type === 'media.catalog.submit'
    ? { kind: 'media.catalog', payload: request.payload }
    : { kind: 'media.watch', payload: request.payload };
}

function queuedResult(request: SubmissionRequest, error: unknown): MediaBackgroundResponse {
  return request.type === 'media.catalog.submit'
    ? { status: 'queued', acceptedEpisodeCount: 0, reason: failureMessage(error) }
    : { status: 'queued' };
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
  queue: DeliveryQueue,
  logger: ExtensionLogger,
): Promise<MessageResult<MediaBackgroundResponse>> {
  try {
    const result = await submitObservation(apiClient, request);
    logSubmission(logger, request, result);
    return messageSuccess(result, request.correlationId);
  } catch (error) {
    if (!queueableError(error)) {
      logger.error('Media request failed', error);
      return failureResult(request, error);
    }
    await queue.enqueue(queuedDelivery(request));
    logger.warn('Observation queued for later delivery', {
      type: request.type,
      reason: failureMessage(error),
    });
    return messageSuccess(queuedResult(request, error), request.correlationId);
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
  queue: DeliveryQueue,
  logger: ExtensionLogger,
): MediaRequestHandler {
  async function deliver(delivery: QueuedDelivery): Promise<void> {
    if (delivery.kind === 'media.catalog') {
      await apiClient.submitCatalog(delivery.payload);
      return;
    }
    await apiClient.submitWatch(delivery.payload);
  }

  return {
    async handle(request, tabId) {
      const requestLogger = logger.child({ tabId, correlationId: request.correlationId });
      return request.type === 'media.watch.resolve'
        ? handleResolution(request, apiClient, requestLogger)
        : handleSubmission(request, apiClient, queue, requestLogger);
    },

    async drainQueue() {
      const batch = await queue.readBatch(10);
      const succeeded: string[] = [];
      const failed: string[] = [];
      for (const item of batch) {
        try {
          await deliver(item.delivery);
          succeeded.push(item.id);
        } catch {
          failed.push(item.id);
        }
      }
      await queue.markSucceeded(succeeded);
      await queue.markFailed(failed);
      const remaining = (await queue.readBatch(50)).length;
      if (batch.length > 0) logger.info('Delivery queue processed', {
        attempted: batch.length,
        delivered: succeeded.length,
        remaining,
      });
      return { delivered: succeeded.length, remaining };
    },
  };
}

export const mediaRequestHandler = createMediaRequestHandler(
  backgroundCantaroApiClient,
  browserDeliveryQueue,
  createExtensionLogger({ scope: 'background', feature: 'media' }),
);
