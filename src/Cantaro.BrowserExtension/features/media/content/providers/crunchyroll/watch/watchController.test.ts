import { parseHTML } from 'linkedom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ContentScriptContext } from 'wxt/utils/content-script-context';

vi.mock('../../../../../../platform/messaging/tabContext', () => ({
  registerTabContext: () => () => undefined,
}));

import { createCrunchyrollWatchController } from './watchController';

function createContext(): ContentScriptContext {
  const invalidationCallbacks: Array<() => void> = [];
  return {
    isInvalid: false,
    setTimeout: (callback: () => void) => window.setTimeout(callback, 0),
    addEventListener: () => undefined,
    onInvalidated: (callback: () => void) => invalidationCallbacks.push(callback),
    abort: () => invalidationCallbacks.forEach(callback => callback()),
  } as unknown as ContentScriptContext;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('Crunchyroll watch controller consent gate', () => {
  it('does not inspect the player before consent and clears state when revoked', async () => {
    const page = parseHTML(`
      <html><body>
        <h1>My Show</h1>
        <video></video>
      </body></html>
    `);
    vi.stubGlobal('window', page.window);
    vi.stubGlobal('document', page.document);
    vi.stubGlobal('location', new URL('https://www.crunchyroll.com/watch/EPISODE1/episode'));
    vi.stubGlobal('MutationObserver', page.window.MutationObserver);
    vi.stubGlobal('Element', page.window.Element);

    let resolveConsent!: (allowed: boolean) => void;
    const consent = new Promise<boolean>(resolve => { resolveConsent = resolve; });
    let consentListener: ((allowed: boolean) => void) | undefined;
    const querySelector = vi.spyOn(page.document, 'querySelector');
    const controller = createCrunchyrollWatchController(createContext(), {
      isTrackingPaused: async () => false,
      watchTrackingPause: () => () => undefined,
      readVerboseLogging: async () => false,
      watchVerboseLogging: () => () => undefined,
      readCollectionConsent: () => consent,
      watchCollectionConsent: listener => {
        consentListener = listener;
        return () => undefined;
      },
      readCatalogCollectionConsent: async () => false,
      watchCatalogCollectionConsent: () => () => undefined,
      submitWatch: async () => {
        throw new Error('watch submission must remain gated');
      },
      resolveWatch: async () => ({ ok: true, correlationId: '', value: { resolved: true } }),
      showResolution: () => ({ close: () => undefined }),
    });

    await new Promise(resolve => setTimeout(resolve, 10));
    expect(querySelector).not.toHaveBeenCalled();
    expect(controller.getSnapshot()).toMatchObject({ pageUrl: '', status: 'starting' });

    resolveConsent(true);
    await new Promise(resolve => setTimeout(resolve, 20));
    consentListener?.(false);
    expect(controller.getSnapshot()).toMatchObject({
      pageUrl: '',
      status: 'starting',
      providerEpisodeId: undefined,
      seriesTitle: undefined,
      episodeTitle: undefined,
    });
    controller.dispose();
  });
});
