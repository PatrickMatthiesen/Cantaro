import { afterEach, describe, expect, it, vi } from 'vitest';
import type { AuthService } from '../auth/authService';
import type { SettingsRepository } from '../settings/settingsRepository';
import { ApiError } from './apiError';
import { createCantaroApiClient } from './cantaroApiClient';

const settingsRepository: SettingsRepository = {
  read: vi.fn(async () => ({
    baseUrl: 'https://cantaro.example.test',
    injectLyricsOnYouTube: false,
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
});
