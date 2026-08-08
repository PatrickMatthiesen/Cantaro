import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../platform/api/apiError';
import type { CantaroApiClient } from '../../../platform/api/cantaroApiClient';
import type { ExtensionLogger } from '../../../platform/diagnostics/logger';
import type { DeliveryQueue, QueuedDelivery } from '../../../platform/storage/deliveryQueue';
import type { SeriesCatalogObservation } from '../contracts/catalogObservation';
import type { WatchProgressObservation, WatchSubmissionResult } from '../contracts/watchObservation';
import { createMediaRequestHandler } from './mediaRequestHandler';

function catalog(): SeriesCatalogObservation {
  return {
    schemaVersion: 1,
    provider: 'crunchyroll',
    providerSeriesId: 'SERIES',
    seriesTitle: 'Series',
    seasonTitle: 'Season 1',
    seriesUrl: 'https://www.crunchyroll.com/series/SERIES/series',
    episodes: [{
      providerEpisodeId: 'EPISODE',
      providerUrl: 'https://www.crunchyroll.com/watch/EPISODE/episode',
      episodeNumber: 1,
    }],
    observedAt: '2026-08-08T00:00:00.000Z',
    extensionVersion: '0.1.0',
  };
}

function apiClient(submitCatalog: CantaroApiClient['submitCatalog']): CantaroApiClient {
  return {
    request: vi.fn(),
    submitCatalog,
    submitWatch: vi.fn(),
    resolveWatch: vi.fn(),
  };
}

function watch(): WatchProgressObservation {
  return {
    schemaVersion: 1,
    provider: 'crunchyroll',
    providerEpisodeId: 'EPISODE',
    observedUrl: 'https://www.crunchyroll.com/watch/EPISODE/episode',
    seriesTitle: 'Series',
    episodeTitle: 'Episode 1',
    episodeNumber: 1,
    watchProgressPercent: 90,
    durationSeconds: 1200,
    positionSeconds: 1080,
    observedAt: '2026-08-08T00:00:00.000Z',
    extensionVersion: '0.1.0',
  };
}

function queue() {
  const deliveries: QueuedDelivery[] = [];
  const value: DeliveryQueue = {
    enqueue: vi.fn(async (delivery) => {
      deliveries.push(delivery);
      return { id: 'queued', delivery, queuedAt: 'now', attempts: 0 };
    }),
    readBatch: vi.fn(async () => []),
    markSucceeded: vi.fn(),
    markFailed: vi.fn(),
  };
  return { deliveries, value };
}

const logger: ExtensionLogger = {
  debug: vi.fn(),
  info: vi.fn(),
  warn: vi.fn(),
  error: vi.fn(),
  child() { return this; },
};

describe('mediaRequestHandler', () => {
  it('treats pending catalog matching as successful API delivery', async () => {
    const deliveryQueue = queue();
    const handler = createMediaRequestHandler(apiClient(async () => ({
      status: 'pending_match',
      observationId: 'observation',
      acceptedEpisodeCount: 0,
    })), deliveryQueue.value, logger);

    const result = await handler.handle({
      type: 'media.catalog.submit',
      correlationId: 'correlation',
      payload: catalog(),
    });

    expect(result).toEqual(expect.objectContaining({
      ok: true,
      value: expect.objectContaining({ status: 'pending_match' }),
    }));
    expect(deliveryQueue.deliveries).toHaveLength(0);
  });

  it('queues catalog evidence when signed out', async () => {
    const deliveryQueue = queue();
    const handler = createMediaRequestHandler(apiClient(async () => {
      throw new ApiError('Sign in first.', 401, false);
    }), deliveryQueue.value, logger);

    const result = await handler.handle({
      type: 'media.catalog.submit',
      correlationId: 'correlation',
      payload: catalog(),
    });

    expect(result).toEqual(expect.objectContaining({
      ok: true,
      value: expect.objectContaining({ status: 'queued' }),
    }));
    expect(deliveryQueue.deliveries).toHaveLength(1);
  });

  it('does not queue a rejected catalog payload', async () => {
    const deliveryQueue = queue();
    const handler = createMediaRequestHandler(apiClient(async () => {
      throw new ApiError('Unsafe provider URL.', 400, false);
    }), deliveryQueue.value, logger);

    const result = await handler.handle({
      type: 'media.catalog.submit',
      correlationId: 'correlation',
      payload: catalog(),
    });

    expect(result).toEqual(expect.objectContaining({
      ok: false,
      error: expect.objectContaining({ code: 'api_rejected' }),
    }));
    expect(deliveryQueue.deliveries).toHaveLength(0);
  });

  it('removes replayed pending-resolution watches after the API owns their review', async () => {
    const delivery = { kind: 'media.watch' as const, payload: watch() };
    const deliveryQueue: DeliveryQueue = {
      enqueue: vi.fn(),
      readBatch: vi.fn()
        .mockResolvedValueOnce([{ id: 'queued-watch', delivery, queuedAt: 'now', attempts: 0 }])
        .mockResolvedValueOnce([]),
      markSucceeded: vi.fn(),
      markFailed: vi.fn(),
    };
    const client = apiClient(vi.fn());
    client.submitWatch = vi.fn(async (): Promise<WatchSubmissionResult> => ({
      status: 'pending_resolution',
      observationId: 'observation',
    }));
    const handler = createMediaRequestHandler(client, deliveryQueue, logger);

    await expect(handler.drainQueue()).resolves.toEqual({ delivered: 1, remaining: 0 });
    expect(deliveryQueue.markSucceeded).toHaveBeenCalledWith(['queued-watch']);
    expect(deliveryQueue.markFailed).toHaveBeenCalledWith([]);
  });
});
