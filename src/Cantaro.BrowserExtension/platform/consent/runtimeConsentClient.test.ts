import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  getRuntimeConsentStatus,
  readContentConsentStatus,
  revokeRuntimeConsent,
  saveRuntimeConsent,
  subscribeRuntimeConsent,
  watchContentConsentStatus,
} from './runtimeConsentClient';

describe('runtimeConsentClient', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('uses redacted runtime messages for reads, writes and revocation', async () => {
    const listener = vi.fn();
    const removeListener = vi.fn();
    const addListener = vi.fn((next: (message: unknown) => void) => { listener.mockImplementation(next); });
    const sendMessage = vi.fn(async (message: { correlationId: string }) => ({
      ok: true,
      correlationId: message.correlationId,
      value: {
        consentVersion: 1, needsReview: false, authenticated: true,
        watchTrackingAllowed: true, catalogCollectionAllowed: false,
      },
    }));
    vi.stubGlobal('browser', { runtime: { sendMessage, onMessage: { addListener, removeListener } } });

    await expect(getRuntimeConsentStatus('https://api.example.test')).resolves.toMatchObject({ authenticated: true });
    await expect(saveRuntimeConsent('https://api.example.test', { watchTracking: true, catalogCollection: false }))
      .resolves.toMatchObject({ watchTrackingAllowed: true });
    await revokeRuntimeConsent('https://api.example.test');
    expect(sendMessage).toHaveBeenCalledWith(expect.objectContaining({ type: 'consent.revoke' }));

    const stop = subscribeRuntimeConsent(listener);
    listener({ type: 'consent.changed', payload: { authenticated: false, watchTrackingAllowed: false, catalogCollectionAllowed: false, consentVersion: 1, needsReview: true } });
    expect(listener).toHaveBeenCalled();
    stop();
    expect(removeListener).toHaveBeenCalled();
  });

  it('maps the runtime state to the purpose-specific content contract', async () => {
    const sendMessage = vi.fn(async () => ({ ok: true, correlationId: 'id', value: {
      consentVersion: 1, needsReview: false, authenticated: true,
      watchTrackingAllowed: true, catalogCollectionAllowed: false,
    } }));
    vi.stubGlobal('browser', { runtime: { sendMessage, onMessage: { addListener: vi.fn(), removeListener: vi.fn() } } });
    await expect(readContentConsentStatus('https://api.example.test'))
      .resolves.toEqual({ authenticated: true, watch: true, catalog: false });
    const stop = watchContentConsentStatus(vi.fn());
    stop();
  });

  it('rechecks before re-enabling collection after an allow notification', async () => {
    let runtimeListener: ((message: unknown) => void) | undefined;
    const sendMessage = vi.fn(async (message: { type: string }) => ({
      ok: true, correlationId: 'id', value: message.type === 'consent.status'
        ? {
          consentVersion: 1, needsReview: true, authenticated: false,
          watchTrackingAllowed: false, catalogCollectionAllowed: false,
        }
        : {
          consentVersion: 1, needsReview: false, authenticated: true,
          watchTrackingAllowed: true, catalogCollectionAllowed: false,
        },
    }));
    const onMessage = {
      addListener: vi.fn((listener: (message: unknown) => void) => { runtimeListener = listener; }),
      removeListener: vi.fn(),
    };
    vi.stubGlobal('browser', { runtime: { sendMessage, onMessage } });
    const received: boolean[] = [];
    const stop = subscribeRuntimeConsent(status => received.push(status.watchTrackingAllowed));
    runtimeListener?.({ type: 'consent.changed', payload: {
      consentVersion: 1, needsReview: false, authenticated: true,
      watchTrackingAllowed: true, catalogCollectionAllowed: false,
    } });
    await vi.waitFor(() => expect(received).toEqual([false, false]));
    stop();
  });
});
