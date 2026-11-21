import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AuthService } from '../auth/authService';
import type { SettingsRepository } from '../settings/settingsRepository';
import { ApiError } from './apiError';
import { createCantaroApiClient } from './cantaroApiClient';
import type { SeriesCatalogObservation } from '../../features/media/contracts/catalogObservation';
import type { WatchProgressObservation } from '../../features/media/contracts/watchObservation';

const settingsRepository: SettingsRepository = {
  read: vi.fn(async () => ({
    baseUrl: 'https://cantaro.example.test',
    verboseLogging: false,
  })),
  save: vi.fn(),
};

const watchObservation: WatchProgressObservation = {
  schemaVersion: 1,
  provider: 'crunchyroll',
  providerEpisodeId: 'EPISODE',
  observedUrl: 'https://www.crunchyroll.com/watch/EPISODE/title?token=secret#player',
  nextEpisodeUrl: 'https://www.crunchyroll.com/watch/NEXT/title?token=secret#player',
  seriesTitle: 'Series',
  episodeTitle: 'Episode',
  watchProgressPercent: 85,
  durationSeconds: 100,
  positionSeconds: 85,
  observedAt: '2026-09-05T00:00:00.000Z',
  extensionVersion: '0.1.0',
};

function authService(getAccessToken: AuthService['getAccessToken']): AuthService {
  return {
    beginInteractiveSignIn: vi.fn(),
    getAccessToken,
    getVerifiedUser: vi.fn(),
    signOut: vi.fn(),
  };
}

describe('cantaroApiClient', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('refreshes once after an unauthorized response', async () => {
    const getAccessToken = vi.fn()
      .mockResolvedValueOnce('stale')
      .mockResolvedValueOnce('fresh');
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ value: 1 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await createCantaroApiClient(
      settingsRepository,
      authService(getAccessToken),
    ).request<{ value: number }>('/api/test');

    expect(result.value).toBe(1);
    expect(getAccessToken).toHaveBeenNthCalledWith(2, 'https://cantaro.example.test', true);
    expect(fetchMock.mock.calls[1]![1].headers.Authorization).toBe('Bearer fresh');
  });

  it('marks a network failure as retryable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    const client = createCantaroApiClient(
      settingsRepository,
      authService(vi.fn(async () => 'token')),
    );

    await expect(client.request('/api/test')).rejects.toEqual(expect.objectContaining({
      name: 'ApiError',
      status: null,
      retryable: true,
    } satisfies Partial<ApiError>));
  });

  it('checks consent after token refresh and before sending the request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const guard = vi.fn(async () => { throw new ApiError('Consent revoked', 403, false); });
    const client = createCantaroApiClient(
      settingsRepository,
      authService(vi.fn(async () => 'token')),
      guard,
    );
    await expect(client.request('/api/media/observations')).rejects.toMatchObject({ status: 403 });
    expect(guard).toHaveBeenCalledWith('/api/media/observations', 'https://cantaro.example.test', {});
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    { observationId: 'observation' },
    { observationId: null },
    {},
  ])('strips private watch URL data and accepts observation ID $observationId', async (response) => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      ...response,
      matchStatus: 'matched',
      wasDeduplicated: false,
      progressUpdated: false,
      requiresResolution: false,
      suggestedEpisodeOffset: 0,
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await createCantaroApiClient(settingsRepository, authService(vi.fn(async () => 'token')))
      .submitWatch(watchObservation);

    expect(result.status).toBe('accepted');
    expect(result.observationId).toBe(response.observationId ?? undefined);
    expect(result.resolution).toBeUndefined();
    const body = JSON.parse(fetchMock.mock.calls[0]![1].body as string) as Record<string, unknown>;
    expect(body.observedUrl).toBe('https://www.crunchyroll.com/watch/EPISODE/title');
    expect(body.nextEpisodeUrl).toBe('https://www.crunchyroll.com/watch/NEXT/title');
  });

  it('preserves the observation version in the watch response and resolve request', async () => {
    const updatedAt = '2026-11-21T10:20:30.123456+00:00';
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(Response.json({
        observationId: 'observation',
        matchStatus: 'ambiguous',
        requiresResolution: true,
        suggestedEpisodeOffset: 0,
        observation: { observedTitle: 'Episode', updatedAt, candidates: [] },
        providerChoices: [],
      }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const client = createCantaroApiClient(settingsRepository, authService(vi.fn(async () => 'token')));

    const { resolution } = await client.submitWatch(watchObservation);
    expect(resolution?.updatedAt).toBe(updatedAt);
    await client.resolveWatch({
      observationId: 'observation', candidateId: 'candidate', episodeOffset: 0,
      expectedUpdatedAt: resolution?.updatedAt,
    });

    const body = JSON.parse(fetchMock.mock.calls[1]![1].body as string);
    expect(body).toEqual({ candidateId: 'candidate', episodeOffset: 0, expectedUpdatedAt: updatedAt });
  });

  it('strips query strings and fragments before transmitting catalog URLs', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 'accepted',
      recordedEpisodeCount: 1,
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    vi.stubGlobal('fetch', fetchMock);

    const observation: SeriesCatalogObservation = {
      schemaVersion: 1,
      provider: 'crunchyroll',
      providerSeriesId: 'SERIES',
      seriesTitle: 'Series',
      seasonTitle: 'Season 1',
      seriesUrl: 'https://www.crunchyroll.com/series/SERIES/title?token=secret#series',
      episodes: [{
        providerEpisodeId: 'EPISODE',
        providerUrl: 'https://www.crunchyroll.com/watch/EPISODE/title?token=secret#episode',
        episodeNumber: 1,
      }],
      observedAt: '2026-09-05T00:00:00.000Z',
      extensionVersion: '0.1.0',
    };

    await createCantaroApiClient(settingsRepository, authService(vi.fn(async () => 'token')))
      .submitCatalog(observation);

    const body = JSON.parse(fetchMock.mock.calls[0]![1].body as string) as SeriesCatalogObservation;
    expect(body.seriesUrl).toBe('https://www.crunchyroll.com/series/SERIES/title');
    expect(body.episodes[0]?.providerUrl)
      .toBe('https://www.crunchyroll.com/watch/EPISODE/title');
  });
});
