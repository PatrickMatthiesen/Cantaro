import { afterEach, describe, expect, it, vi } from 'vitest';
import { runtimeAccessTokenProvider, verifyRuntimeSession } from './runtimeAuthClient';

describe('runtimeAuthClient', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('asks the background worker for access tokens instead of refreshing locally', async () => {
    const sendMessage = vi.fn(async (message: { correlationId: string }) => ({
      ok: true,
      value: { accessToken: 'background-token' },
      correlationId: message.correlationId,
    }));
    vi.stubGlobal('browser', { runtime: { sendMessage } });

    await expect(runtimeAccessTokenProvider.getAccessToken('https://api.example.test', true))
      .resolves.toBe('background-token');
    expect(sendMessage).toHaveBeenCalledWith(expect.objectContaining({
      type: 'auth.token.get',
      payload: { apiBaseUrl: 'https://api.example.test', forceRefresh: true },
    }));
  });

  it('asks the background worker to verify the stored session', async () => {
    const sendMessage = vi.fn(async (message: { correlationId: string }) => ({
      ok: true,
      value: { user: { email: 'user@example.test' } },
      correlationId: message.correlationId,
    }));
    vi.stubGlobal('browser', { runtime: { sendMessage } });

    await expect(verifyRuntimeSession('https://api.example.test'))
      .resolves.toEqual({ email: 'user@example.test' });
    expect(sendMessage).toHaveBeenCalledWith(expect.objectContaining({
      type: 'auth.session.verify',
    }));
  });

  it('asks the background worker to coordinate sign-out', async () => {
    const sendMessage = vi.fn(async (message: { correlationId: string }) => ({
      ok: true,
      value: { signedOut: true },
      correlationId: message.correlationId,
    }));
    vi.stubGlobal('browser', { runtime: { sendMessage } });

    const { signOutRuntimeSession } = await import('./runtimeAuthClient');
    await signOutRuntimeSession('https://api.example.test');

    expect(sendMessage).toHaveBeenCalledWith(expect.objectContaining({
      type: 'auth.session.signOut',
    }));
  });
});
