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

  it('strips query strings and fragments before transmitting watch URLs', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      observationId: 'observation',
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

    const observation: WatchProgressObservation = {
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

    await createCantaroApiClient(settingsRepository, authService(vi.fn(async () => 'token')))
      .submitWatch(observation);

    const body = JSON.parse(fetchMock.mock.calls[0]![1].body as string) as Record<string, unknown>;
    expect(body.observedUrl).toBe('https://www.crunchyroll.com/watch/EPISODE/title');
    expect(body.nextEpisodeUrl).toBe('https://www.crunchyroll.com/watch/NEXT/title');
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
