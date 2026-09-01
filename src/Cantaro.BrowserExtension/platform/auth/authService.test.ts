import { afterEach, describe, expect, it, vi } from 'vitest';
import { createAuthService } from './authService';
import type { ExtensionSession } from './extensionSession';
import type { SessionRepository } from './sessionRepository';

describe('authService', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('uses a fresh session without starting a refresh grant', async () => {
    const session: ExtensionSession = {
      baseUrl: 'https://api.example.test',
      accessToken: 'fresh-access',
      refreshToken: 'refresh',
      accessTokenExpiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
      email: 'user@example.test',
    };
    const repository: SessionRepository = {
      read: vi.fn(async () => session),
      save: vi.fn(async value => value),
      clear: vi.fn(),
    };
    const launchWebAuthFlow = vi.fn();
    const service = createAuthService(repository, {
      runtimeId: () => 'extension-id',
      redirectUrl: () => 'https://extension.example.test/callback',
      launchWebAuthFlow,
    });

    expect(await service.getAccessToken('https://api.example.test'))
      .toBe('fresh-access');
    expect(launchWebAuthFlow).not.toHaveBeenCalled();
  });

  it('keeps a newer rotated session when an older refresh attempt loses the race', async () => {
    const stale = expiredSession('stale-access', 'stale-refresh');
    const rotated = expiredSession('rotated-access', 'rotated-refresh');
    const repository: SessionRepository = {
      read: vi.fn()
        .mockResolvedValueOnce(stale)
        .mockResolvedValueOnce(rotated),
      save: vi.fn(async value => value),
      clear: vi.fn(),
    };
    vi.stubGlobal('fetch', vi.fn(async () => new Response(
      JSON.stringify({ error: 'invalid_grant' }),
      { status: 400, headers: { 'Content-Type': 'application/json' } },
    )));

    const service = createAuthService(repository, authBrowser());

    expect(await service.getAccessToken('https://api.example.test')).toBe('rotated-access');
    expect(repository.clear).not.toHaveBeenCalled();
  });

  it('preserves the refresh session when the token service is temporarily unreachable', async () => {
    const session = expiredSession('expired-access', 'retryable-refresh');
    const repository: SessionRepository = {
      read: vi.fn(async () => session),
      save: vi.fn(async value => value),
      clear: vi.fn(),
    };
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('offline'); }));

    const service = createAuthService(repository, authBrowser());

    expect(await service.getAccessToken('https://api.example.test')).toBeNull();
    expect(repository.clear).not.toHaveBeenCalled();
  });

  it('rotates refresh and access tokens after a browser restart', async () => {
    let stored = expiredSession('', 'persistent-refresh');
    const repository: SessionRepository = {
      read: vi.fn(async () => stored),
      save: vi.fn(async value => {
        stored = value;
        return value;
      }),
      clear: vi.fn(),
    };
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({
      access_token: 'rotated-access',
      refresh_token: 'rotated-refresh',
      expires_in: 3600,
      user: { email: 'user@example.test' },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })));

    const service = createAuthService(repository, authBrowser());

    await expect(service.getAccessToken('https://api.example.test')).resolves.toBe('rotated-access');
    expect(repository.save).toHaveBeenCalledWith(expect.objectContaining({
      accessToken: 'rotated-access',
      refreshToken: 'rotated-refresh',
    }));
  });

  it('clears a refresh session rejected by the token service', async () => {
    const session = expiredSession('', 'revoked-refresh');
    const repository: SessionRepository = {
      read: vi.fn(async () => session),
      save: vi.fn(async value => value),
      clear: vi.fn(),
    };
    vi.stubGlobal('fetch', vi.fn(async () => new Response(
      JSON.stringify({ error: 'invalid_grant' }),
      { status: 400, headers: { 'Content-Type': 'application/json' } },
    )));

    const service = createAuthService(repository, authBrowser());

    await expect(service.getAccessToken('https://api.example.test')).resolves.toBeNull();
    expect(repository.clear).toHaveBeenCalledOnce();
  });

  it('revokes and clears the persistent session on sign-out', async () => {
    const session = expiredSession('access', 'refresh-to-revoke');
    const repository: SessionRepository = {
      read: vi.fn(async () => session),
      save: vi.fn(async value => value),
      clear: vi.fn(),
    };
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const service = createAuthService(repository, authBrowser());

    await service.signOut('https://api.example.test');

    expect(fetchMock).toHaveBeenCalledWith(
      'https://api.example.test/api/auth/extension/revoke',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(repository.clear).toHaveBeenCalledOnce();
  });

  it('does not recreate a session cleared while refresh was in flight', async () => {
    let stored: ExtensionSession | null = expiredSession('expired-access', 'refresh-before-signout');
    let resolveGrant: ((response: Response) => void) | undefined;
    const repository: SessionRepository = {
      read: vi.fn(async () => stored),
      save: vi.fn(async value => {
        stored = value;
        return value;
      }),
      clear: vi.fn(async () => { stored = null; }),
    };
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { resolveGrant = resolve; })));
    const service = createAuthService(repository, authBrowser());

    const refreshing = service.getAccessToken('https://api.example.test');
    await vi.waitFor(() => expect(resolveGrant).toBeTypeOf('function'));
    stored = null;
    resolveGrant?.(new Response(JSON.stringify({
      access_token: 'rotated-access',
      refresh_token: 'rotated-refresh',
      expires_in: 3600,
      user: { email: 'user@example.test' },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await expect(refreshing).resolves.toBeNull();
    expect(repository.save).not.toHaveBeenCalled();
    expect(stored).toBeNull();
  });
});

function expiredSession(accessToken: string, refreshToken: string): ExtensionSession {
  return {
    baseUrl: 'https://api.example.test',
    accessToken,
    refreshToken,
    accessTokenExpiresAt: new Date(Date.now() - 60_000).toISOString(),
    email: 'user@example.test',
  };
}

function authBrowser() {
  return {
    runtimeId: () => 'extension-id',
    redirectUrl: () => 'https://extension.example.test/callback',
    launchWebAuthFlow: vi.fn(),
  };
}
