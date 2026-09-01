import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import { registerTabContext } from './tabContext';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('registerTabContext', () => {
  it('serves the local snapshot and emits change notifications without storage', async () => {
    let messageListener: ((message: unknown) => unknown) | undefined;
    const sendMessage = vi.fn().mockResolvedValue(undefined);
    const removeListener = vi.fn();
    vi.stubGlobal('browser', {
      runtime: {
        sendMessage,
        onMessage: {
          addListener: vi.fn(listener => { messageListener = listener; }),
          removeListener,
        },
      },
    } as unknown as typeof browser);
    let invalidate: (() => void) | undefined;
    const ctx = {
      onInvalidated(listener: () => void) {
        invalidate = listener;
        return () => undefined;
      },
    } as unknown as ContentScriptContext;
    const snapshot = {
      feature: 'media' as const,
      provider: 'crunchyroll' as const,
      pageKind: 'series' as const,
      pageUrl: 'https://www.crunchyroll.com/series/example',
      status: 'starting' as const,
      observedEpisodeCount: 0,
    };

    const notifyChanged = registerTabContext(ctx, () => snapshot);
    await expect(messageListener?.({ type: 'tab.context.get', correlationId: 'request-1' }))
      .resolves.toEqual({ ok: true, value: snapshot, correlationId: 'request-1' });

    notifyChanged();
    expect(sendMessage).toHaveBeenCalledWith({ type: 'tab.context.changed' });
    invalidate?.();
    expect(removeListener).toHaveBeenCalledWith(messageListener);
  });

  it('does not answer tab-context requests while its route is inactive', () => {
    let messageListener: ((message: unknown) => unknown) | undefined;
    vi.stubGlobal('browser', {
      runtime: {
        sendMessage: vi.fn().mockResolvedValue(undefined),
        onMessage: {
          addListener: vi.fn(listener => { messageListener = listener; }),
          removeListener: vi.fn(),
        },
      },
    } as unknown as typeof browser);
    const ctx = {
      onInvalidated: vi.fn(),
    } as unknown as ContentScriptContext;

    registerTabContext(ctx, () => ({
      feature: 'media',
      provider: 'crunchyroll',
      pageKind: 'series',
      pageUrl: 'https://www.crunchyroll.com/series/example',
      status: 'starting',
      observedEpisodeCount: 0,
    }), () => false);

    expect(messageListener?.({ type: 'tab.context.get', correlationId: 'request-2' }))
      .toBeUndefined();
  });
});
