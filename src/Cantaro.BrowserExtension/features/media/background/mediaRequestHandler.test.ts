import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../platform/api/apiError';
import type { CantaroApiClient } from '../../../platform/api/cantaroApiClient';
import type { ExtensionLogger } from '../../../platform/diagnostics/logger';
import type { SeriesCatalogObservation } from '../contracts/catalogObservation';
import type { WatchProgressObservation, WatchSubmissionResult } from '../contracts/watchObservation';
import { createMediaRequestHandler } from './mediaRequestHandler';
import type { MediaProgressNotifier } from './mediaProgressNotifier';

const settingsRepository = { read: vi.fn(async () => ({ baseUrl: 'https://api.example.test', verboseLogging: false })), save: vi.fn() };
const consentGate = { getStatus: vi.fn(async () => ({
  consentVersion: 1, needsReview: false, authenticated: true,
  watchTrackingAllowed: true, catalogCollectionAllowed: true,
})) };

function catalog(): SeriesCatalogObservation {
  return {
    schemaVersion: 1, provider: 'crunchyroll', providerSeriesId: 'SERIES',
    seriesTitle: 'Series', seasonTitle: 'Season 1',
    seriesUrl: 'https://www.crunchyroll.com/series/SERIES/series',
    episodes: [{ providerEpisodeId: 'EPISODE', providerUrl: 'https://www.crunchyroll.com/watch/EPISODE/episode', episodeNumber: 1 }],
    observedAt: '2026-08-08T00:00:00.000Z', extensionVersion: '0.1.0',
  };
}

function watch(): WatchProgressObservation {
  return {
    schemaVersion: 1, provider: 'crunchyroll', providerEpisodeId: 'EPISODE',
    observedUrl: 'https://www.crunchyroll.com/watch/EPISODE/episode',
    seriesTitle: 'Series', episodeTitle: 'Episode 1', episodeNumber: 1,
    watchProgressPercent: 90, durationSeconds: 1200, positionSeconds: 1080,
    observedAt: '2026-08-08T00:00:00.000Z', extensionVersion: '0.1.0',
  };
}

function apiClient(submitCatalog: CantaroApiClient['submitCatalog']): CantaroApiClient {
  return { request: vi.fn(), submitCatalog, submitWatch: vi.fn(), resolveWatch: vi.fn() };
}

const logger: ExtensionLogger = {
  debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn(), child() { return this; },
};

describe('mediaRequestHandler', () => {
  it('blocks watch delivery until authentication and watch consent are both active', async () => {
    const client = apiClient(vi.fn());
    const gate = { getStatus: vi.fn(async () => ({
      consentVersion: 1, needsReview: true, authenticated: true,
      watchTrackingAllowed: false, catalogCollectionAllowed: true,
    })) };
    const handler = createMediaRequestHandler(client, logger, undefined, gate, settingsRepository);
    const result = await handler.handle({ type: 'media.watch.submit', correlationId: 'correlation', payload: watch() });
    expect(result).toEqual(expect.objectContaining({ ok: false, error: expect.objectContaining({ code: 'consent_required' }) }));
    expect(client.submitWatch).not.toHaveBeenCalled();
  });

  it('uses separate catalog consent for catalog delivery', async () => {
    const client = apiClient(vi.fn());
    const gate = { getStatus: vi.fn(async () => ({
      consentVersion: 1, needsReview: false, authenticated: true,
      watchTrackingAllowed: true, catalogCollectionAllowed: false,
    })) };
    const handler = createMediaRequestHandler(client, logger, undefined, gate, settingsRepository);
    const result = await handler.handle({ type: 'media.catalog.submit', correlationId: 'correlation', payload: catalog() });
    expect(result).toEqual(expect.objectContaining({ ok: false, error: expect.objectContaining({ code: 'consent_required' }) }));
    expect(client.submitCatalog).not.toHaveBeenCalled();
  });

  it('notifies open Cantaro pages after local progress changes', async () => {
    const client = apiClient(vi.fn());
    client.submitWatch = vi.fn(async (): Promise<WatchSubmissionResult> => ({
      status: 'accepted', observationId: 'observation', matchedMediaTitleId: 'media-title',
      resolvedProgress: 16, progressUpdated: true,
    }));
    const notifier: MediaProgressNotifier = { notify: vi.fn(async () => {}) };
    const handler = createMediaRequestHandler(client, logger, notifier, consentGate, settingsRepository);
    const result = await handler.handle({ type: 'media.watch.submit', correlationId: 'correlation', payload: watch() });
    expect(result).toEqual(expect.objectContaining({ ok: true }));
    expect(notifier.notify).toHaveBeenCalledWith(expect.objectContaining({ matchedMediaTitleId: 'media-title', resolvedProgress: 16 }));
  });

  it('treats pending catalog matching as successful API delivery', async () => {
    const handler = createMediaRequestHandler(apiClient(async () => ({
      status: 'pending_match', observationId: 'observation', acceptedEpisodeCount: 0,
    })), logger, undefined, consentGate, settingsRepository);
    const result = await handler.handle({ type: 'media.catalog.submit', correlationId: 'correlation', payload: catalog() });
    expect(result).toEqual(expect.objectContaining({ ok: true, value: expect.objectContaining({ status: 'pending_match' }) }));
  });

  it('returns an authentication failure without retaining the observation', async () => {
    const handler = createMediaRequestHandler(apiClient(async () => {
      throw new ApiError('Sign in first.', 401, false);
    }), logger, undefined, consentGate, settingsRepository);
    const result = await handler.handle({ type: 'media.catalog.submit', correlationId: 'correlation', payload: catalog() });
    expect(result).toEqual(expect.objectContaining({ ok: false, error: expect.objectContaining({ code: 'not_authenticated' }) }));
  });

  it('returns rejected catalog payloads without retaining them', async () => {
    const handler = createMediaRequestHandler(apiClient(async () => {
      throw new ApiError('Unsafe provider URL.', 400, false);
    }), logger, undefined, consentGate, settingsRepository);
    const result = await handler.handle({ type: 'media.catalog.submit', correlationId: 'correlation', payload: catalog() });
    expect(result).toEqual(expect.objectContaining({ ok: false, error: expect.objectContaining({ code: 'api_rejected' }) }));
  });
});
