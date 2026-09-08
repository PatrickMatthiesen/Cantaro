import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MediaRequestHandler } from '../../features/media/background/mediaRequestHandler';
import { createRuntimeRouter, type RuntimeRouterDependencies } from './runtimeRouter';

const mediaHandler: MediaRequestHandler = {
  handle: vi.fn(),
};

describe('runtimeRouter', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('ignores messages owned by another extension feature', async () => {
    expect(await createRuntimeRouter(mediaHandler)({ type: 'unknown' }, {}))
      .toBeUndefined();
  });

  it('returns only redacted consent state to content scripts', async () => {
    const dependencies = testDependencies();
    dependencies.consentService.getStatus = vi.fn(async () => ({
      consentVersion: 1, needsReview: false, authenticated: true,
      watchTrackingAllowed: true, catalogCollectionAllowed: false,
    }));
    const response = await createRuntimeRouter(mediaHandler, dependencies)(
      { type: 'consent.status', correlationId: 'status', payload: {} },
      { tab: { id: 17 } } as Browser.runtime.MessageSender,
    ) as { value: Record<string, unknown> };
    expect(response.value).toEqual(expect.objectContaining({ authenticated: true, watchTrackingAllowed: true }));
    expect(response.value).not.toHaveProperty('email');
  });

  it('rejects consent changes originating from website content scripts', async () => {
    const dependencies = testDependencies();
    const response = await createRuntimeRouter(mediaHandler, dependencies)(
      { type: 'consent.save', correlationId: 'save', payload: {
        baseUrl: 'https://api.example.test', watchTracking: true, catalogCollection: false,
      } },
      { tab: { id: 17 } } as Browser.runtime.MessageSender,
    ) as { ok: boolean; error: { code: string } };
    expect(response).toEqual(expect.objectContaining({ ok: false, error: expect.objectContaining({ code: 'invalid_request' }) }));
    expect(dependencies.consentService.save).not.toHaveBeenCalled();
  });

  it('broadcasts saved consent to extension pages and open content tabs', async () => {
    const runtimeSendMessage = vi.fn(async () => undefined);
    const tabSendMessage = vi.fn(async () => undefined);
    vi.stubGlobal('browser', {
      runtime: { sendMessage: runtimeSendMessage },
      tabs: { query: vi.fn(async () => [{ id: 4 }, { id: 5 }]), sendMessage: tabSendMessage },
    });
    const dependencies = testDependencies();
    dependencies.consentService.save = vi.fn(async () => ({
      consentVersion: 1, needsReview: false, authenticated: true,
      watchTrackingAllowed: true, catalogCollectionAllowed: false,
    }));
    await createRuntimeRouter(mediaHandler, dependencies)(
      { type: 'consent.save', correlationId: 'save', payload: {
        baseUrl: 'https://api.example.test', watchTracking: true, catalogCollection: false,
      } },
      {},
    );
    expect(runtimeSendMessage).toHaveBeenCalledWith(expect.objectContaining({ type: 'consent.changed' }));
    await vi.waitFor(() => expect(tabSendMessage.mock.calls.length).toBeGreaterThanOrEqual(2));
  });

  it('broadcasts revocation before waiting for remote sign-out', async () => {
    const events: string[] = [];
    const runtimeSendMessage = vi.fn(() => { events.push('broadcast'); return Promise.resolve(); });
    let releaseSignOut: (() => void) | undefined;
    vi.stubGlobal('browser', {
      runtime: { sendMessage: runtimeSendMessage },
      tabs: { query: vi.fn(async () => []), sendMessage: vi.fn() },
    });
    const dependencies = testDependencies();
    dependencies.consentService.revoke = vi.fn(async () => { events.push('revoke'); });
    dependencies.authService.signOut = vi.fn(() => new Promise<void>(resolve => { releaseSignOut = () => { events.push('signout'); resolve(); }; }));
    const signOut = createRuntimeRouter(mediaHandler, dependencies)(
      { type: 'auth.session.signOut', correlationId: 'signout', payload: { baseUrl: 'https://api.example.test' } },
      {},
    );
    await vi.waitFor(() => expect(events).toEqual(['broadcast', 'revoke']));
    releaseSignOut?.();
    await signOut;
    expect(events).toEqual(['broadcast', 'revoke', 'signout']);
  });

  it('broadcasts deny even when revocation persistence fails', async () => {
    const runtimeSendMessage = vi.fn(async () => undefined);
    vi.stubGlobal('browser', {
      runtime: { sendMessage: runtimeSendMessage },
      tabs: { query: vi.fn(async () => []), sendMessage: vi.fn() },
    });
    const dependencies = testDependencies();
    dependencies.consentService.revoke = vi.fn(async () => { throw new Error('storage unavailable'); });
    const response = await createRuntimeRouter(mediaHandler, dependencies)(
      { type: 'consent.revoke', correlationId: 'revoke', payload: { baseUrl: 'https://api.example.test' } },
      {},
    ) as { ok: boolean };
    expect(response.ok).toBe(false);
    expect(runtimeSendMessage).toHaveBeenCalledWith(expect.objectContaining({
      type: 'consent.changed', payload: expect.objectContaining({ watchTrackingAllowed: false }),
    }));
  });

  it('stops content collection before waiting for revocation storage', async () => {
    const sendMessage = vi.fn(async () => undefined);
    vi.stubGlobal('browser', { runtime: { sendMessage }, tabs: { query: vi.fn(async () => []), sendMessage: vi.fn() } });
    const dependencies = testDependencies();
    let release: () => void = () => {};
    dependencies.consentService.revoke = vi.fn(() => new Promise<void>(resolve => { release = resolve; }));
    const result = createRuntimeRouter(mediaHandler, dependencies)(
      { type: 'consent.revoke', correlationId: 'revoke', payload: { baseUrl: 'https://api.example.test' } }, {},
    );
    await vi.waitFor(() => expect(sendMessage).toHaveBeenCalledWith(expect.objectContaining({
      payload: expect.objectContaining({ watchTrackingAllowed: false, catalogCollectionAllowed: false }),
    })));
    release();
    await result;
  });
});

function testDependencies(): RuntimeRouterDependencies {
  return {
    settingsRepository: { read: vi.fn(async () => ({ baseUrl: 'https://api.example.test', verboseLogging: false })), save: vi.fn() },
    authService: { getAccessToken: vi.fn(async () => 'token'), getVerifiedUser: vi.fn(async () => ({ email: 'user@example.test' })), signOut: vi.fn(async () => {}) },
    consentService: { getStatus: vi.fn(async () => ({ consentVersion: 1, needsReview: true, authenticated: true, watchTrackingAllowed: false, catalogCollectionAllowed: false })), save: vi.fn(async () => ({ consentVersion: 1, needsReview: false, authenticated: true, watchTrackingAllowed: true, catalogCollectionAllowed: true })), revoke: vi.fn(async () => {}) },
  };
}
