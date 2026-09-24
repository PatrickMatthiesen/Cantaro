import { parseHTML } from 'linkedom';
import { afterEach, describe, expect, it, vi } from 'vitest';

const consentMocks = vi.hoisted(() => ({
  getRuntimeConsentStatus: vi.fn(),
  subscribeRuntimeConsent: vi.fn(),
}));

vi.mock('../../../platform/consent/runtimeConsentClient', () => consentMocks);

import { startYouTubeLyrics } from './startYouTubeLyrics';

function createContext() {
  const invalidated: Array<() => void> = [];
  return {
    onInvalidated: (callback: () => void) => invalidated.push(callback),
    invalidate: () => invalidated.forEach(callback => callback()),
  };
}

function consent(authenticated: boolean, musicLyricsAllowed: boolean) {
  return { authenticated, musicLyricsAllowed } as Parameters<Parameters<typeof consentMocks.subscribeRuntimeConsent>[0]>[0];
}

function setupPage(initialUrl: string) {
  const page = parseHTML('<html><body></body></html>');
  let currentUrl = initialUrl;
  let reads = 0;
  Object.defineProperty(page.window, 'location', {
    configurable: true,
    value: {
      get href() {
        reads++;
        return currentUrl;
      },
    },
  });
  vi.stubGlobal('window', page.window);
  vi.stubGlobal('document', page.document);
  vi.stubGlobal('MutationObserver', page.window.MutationObserver);
  return {
    ...page,
    get reads() { return reads; },
    setUrl(url: string) { currentUrl = url; },
  };
}

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe('YouTube lyrics content entrypoint', () => {
  it('does not read page URLs before consent, tracks SPA video changes, and stops on revocation', async () => {
    vi.useFakeTimers();
    const page = setupPage('https://www.youtube.com/watch?v=abcdefghijk');
    let notifyConsent!: (status: ReturnType<typeof consent>) => void;
    const unsubscribe = vi.fn();
    consentMocks.getRuntimeConsentStatus.mockResolvedValue(consent(false, false));
    consentMocks.subscribeRuntimeConsent.mockImplementation((listener: typeof notifyConsent) => {
      notifyConsent = listener;
      return unsubscribe;
    });

    const ctx = createContext();
    startYouTubeLyrics(ctx);
    await vi.runAllTicks();
    expect(page.reads).toBe(0);
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).toBeNull();

    notifyConsent(consent(true, true));
    expect(page.reads).toBe(1);
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).not.toBeNull();

    page.setUrl('https://www.youtube.com/watch?v=lmnopqrstuv');
    await vi.advanceTimersByTimeAsync(500);
    expect(page.reads).toBe(2);

    notifyConsent(consent(true, false));
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).toBeNull();
    const readsAfterRevoke = page.reads;
    page.setUrl('https://www.youtube.com/watch?v=zyxwvutsrqp');
    await vi.advanceTimersByTimeAsync(1500);
    expect(page.reads).toBe(readsAfterRevoke);

    ctx.invalidate();
    expect(unsubscribe).toHaveBeenCalledOnce();
  });

  it('clears its consent-scoped URL poll when the content context is invalidated', async () => {
    vi.useFakeTimers();
    const page = setupPage('https://music.youtube.com/watch?v=abcdefghijk');
    let notifyConsent!: (status: ReturnType<typeof consent>) => void;
    consentMocks.getRuntimeConsentStatus.mockResolvedValue(consent(true, true));
    consentMocks.subscribeRuntimeConsent.mockImplementation((listener: typeof notifyConsent) => {
      notifyConsent = listener;
      return () => undefined;
    });
    const ctx = createContext();
    startYouTubeLyrics(ctx);
    await vi.runAllTicks();
    expect(page.reads).toBe(1);
    ctx.invalidate();
    page.setUrl('https://music.youtube.com/watch?v=lmnopqrstuv');
    await vi.advanceTimersByTimeAsync(1500);
    expect(page.reads).toBe(1);
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).toBeNull();
    notifyConsent(consent(true, true));
    expect(page.reads).toBe(1);
  });
});
