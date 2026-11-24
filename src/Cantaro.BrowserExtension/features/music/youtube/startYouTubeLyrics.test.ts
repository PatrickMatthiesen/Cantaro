import { parseHTML } from 'linkedom';
import { afterEach, describe, expect, it, vi } from 'vitest';

const consentMocks = vi.hoisted(() => ({
  getRuntimeConsentStatus: vi.fn(),
  subscribeRuntimeConsent: vi.fn(),
}));

vi.mock('../../../platform/consent/runtimeConsentClient', () => consentMocks);
vi.mock('./drawer', async importOriginal => {
  const drawer = await importOriginal<typeof import('./drawer')>();
  return {
    ...drawer,
    mountYouTubeLyricsDrawer: (options: Parameters<typeof drawer.mountYouTubeLyricsDrawer>[0]) =>
      drawer.mountYouTubeLyricsDrawer({ ...options, shadowMode: 'open' }),
  };
});

import { startYouTubeLyrics } from './startYouTubeLyrics';
import type { YouTubeLyricsRequest } from './youtubeLyrics';

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

function setupPage(initialUrl: string, html = '<html><body></body></html>') {
  const page = parseHTML(html);
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
  it('mounts after authenticated consent, reuses the host across Mix videos, and stops on revocation', async () => {
    vi.useFakeTimers();
    const page = setupPage('https://www.youtube.com/watch?v=abcdefghijk&list=RDabcdefghijk&index=1');
    let notifyConsent!: (status: ReturnType<typeof consent>) => void;
    const unsubscribe = vi.fn();
    consentMocks.getRuntimeConsentStatus.mockResolvedValue(consent(false, true));
    consentMocks.subscribeRuntimeConsent.mockImplementation((listener: typeof notifyConsent) => {
      notifyConsent = listener;
      return unsubscribe;
    });
    const sendMessage = vi.fn(async (_request: YouTubeLyricsRequest) => ({
      ok: true,
      value: { song: null, lyrics: null },
    }));
    vi.stubGlobal('browser', { runtime: { sendMessage } });

    const ctx = createContext();
    startYouTubeLyrics(ctx);
    await vi.advanceTimersByTimeAsync(0);
    expect(page.reads).toBe(0);
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).toBeNull();

    notifyConsent(consent(true, false));
    expect(page.reads).toBe(0);
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).toBeNull();

    notifyConsent(consent(true, true));
    expect(page.reads).toBe(1);
    const host = page.document.querySelector<HTMLElement>('[data-cantaro-youtube-lyrics]')!;
    expect(host?.parentElement).toBe(page.document.body);
    expect(host.getAttribute('data-placement')).toBe('floating');
    host.shadowRoot!.querySelector('.launcher')!.dispatchEvent(new page.window.Event('click'));
    await vi.advanceTimersByTimeAsync(0);

    page.setUrl('https://www.youtube.com/watch?v=lmnopqrstuv&list=RDabcdefghijk&index=2');
    await vi.advanceTimersByTimeAsync(500);
    expect(page.reads).toBe(2);
    const hosts = page.document.querySelectorAll('[data-cantaro-youtube-lyrics]');
    expect(hosts).toHaveLength(1);
    expect(hosts[0]).toBe(host);
    expect(host.parentElement).toBe(page.document.body);
    expect(sendMessage.mock.calls.map(([request]) => request.payload)).toEqual([
      { videoId: 'abcdefghijk' }, { videoId: 'lmnopqrstuv' },
    ]);

    notifyConsent(consent(true, false));
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).toBeNull();
    const readsAfterRevoke = page.reads;
    page.setUrl('https://www.youtube.com/watch?v=zyxwvutsrqp');
    await vi.advanceTimersByTimeAsync(1500);
    expect(page.reads).toBe(readsAfterRevoke);

    ctx.invalidate();
    expect(unsubscribe).toHaveBeenCalledOnce();
  });

  it('keeps the floating host through watch metadata replacement and fullscreen', async () => {
    vi.useFakeTimers();
    const page = setupPage('https://www.youtube.com/watch?v=abcdefghijk', `
      <html><body>
        <ytd-watch-flexy>
          <div id="player-full-bleed-container"><ytd-player><video></video></ytd-player></div>
          <div id="below"><ytd-watch-metadata></ytd-watch-metadata></div>
        </ytd-watch-flexy>
      </body></html>
    `);
    consentMocks.getRuntimeConsentStatus.mockResolvedValue(consent(true, true));
    consentMocks.subscribeRuntimeConsent.mockReturnValue(() => undefined);

    const ctx = createContext();
    startYouTubeLyrics(ctx);
    await vi.advanceTimersByTimeAsync(0);
    const host = page.document.querySelector<HTMLElement>('[data-cantaro-youtube-lyrics]')!;
    expect(host?.parentElement).toBe(page.document.body);

    const metadata = page.document.querySelector('ytd-watch-metadata')!;
    metadata.replaceWith(page.document.createElement('ytd-watch-metadata'));
    await vi.advanceTimersByTimeAsync(500);
    expect(page.document.querySelector('[data-cantaro-youtube-lyrics]')).toBe(host);
    expect(host.parentElement).toBe(page.document.body);

    let fullscreenElement = page.document.querySelector('#player-full-bleed-container');
    Object.defineProperty(page.document, 'fullscreenElement', { get: () => fullscreenElement });
    page.document.dispatchEvent(new page.window.Event('fullscreenchange'));
    expect(host.hidden).toBe(true);
    fullscreenElement = null;
    page.document.dispatchEvent(new page.window.Event('fullscreenchange'));
    expect(host.hidden).toBe(false);
    expect(host.parentElement).toBe(page.document.body);
    ctx.invalidate();
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

  it('keeps ad metadata out of lyrics lookups and updates on same-document navigation', async () => {
    vi.useFakeTimers();
    // Player and queue structure captured on Snowfall. Ad metadata is simulated.
    const page = setupPage('https://music.youtube.com/watch?v=abcdefghijk', `
      <html><head><title>Sensodyne - Advertisement - YouTube Music</title></head><body>
        <ytmusic-player-page player-ui-state="PLAYER_PAGE_OPEN"><div>
          <div id="main-panel"><ytmusic-player>
            <div id="movie_player" class="html5-video-player ad-showing"><div class="html5-video-container">
              <video class="video-stream html5-main-video"></video>
            </div></div>
          </ytmusic-player></div>
          <div id="side-panel"><tp-yt-paper-tabs></tp-yt-paper-tabs><ytmusic-tab-renderer></ytmusic-tab-renderer></div>
        </div></ytmusic-player-page>
        <ytmusic-player-bar><div class="content-info-wrapper">
          <yt-formatted-string class="title">Advertisement</yt-formatted-string>
          <yt-formatted-string class="byline">Sensodyne</yt-formatted-string>
        </div></ytmusic-player-bar>
      </body></html>
    `);
    consentMocks.getRuntimeConsentStatus.mockResolvedValue(consent(true, true));
    consentMocks.subscribeRuntimeConsent.mockReturnValue(() => undefined);
    const sendMessage = vi.fn(async ({ payload }: YouTubeLyricsRequest) => ({
      ok: true,
      value: payload.videoId === 'abcdefghijk'
        ? { song: null, lyrics: null }
        : {
          song: { id: 'track:next', title: 'Next song', artist: 'Next artist' },
          lyrics: { state: 'available', plainLyrics: 'Next lyrics', attribution: 'LRCLIB' },
        },
    }));
    vi.stubGlobal('browser', { runtime: { sendMessage } });
    const ctx = createContext();
    startYouTubeLyrics(ctx);
    await vi.advanceTimersByTimeAsync(0);
    const host = page.document.querySelector<HTMLElement>('#side-panel > [data-cantaro-youtube-lyrics]')!;
    const drawer = host.shadowRoot!;
    let fullscreenElement: Element | null = page.document.documentElement;
    Object.defineProperty(page.document, 'fullscreenElement', { get: () => fullscreenElement });
    page.document.dispatchEvent(new page.window.Event('fullscreenchange'));
    expect(host.hidden).toBe(true);
    fullscreenElement = null;
    page.document.dispatchEvent(new page.window.Event('fullscreenchange'));
    expect(host.hidden).toBe(false);
    drawer.querySelector('.launcher')!.dispatchEvent(new page.window.Event('click'));
    await vi.advanceTimersByTimeAsync(0);
    expect(drawer.querySelector<HTMLInputElement>('input[aria-label="Song title"]')!.value ?? '').toBe('');
    expect(drawer.querySelector<HTMLInputElement>('input[aria-label="Artist"]')!.value ?? '').toBe('');
    drawer.querySelector('form')!.dispatchEvent(new page.window.Event('submit', { cancelable: true }));
    expect(sendMessage.mock.calls.map(([request]) => request.payload)).toEqual([{ videoId: 'abcdefghijk' }]);

    page.document.querySelector('#movie_player')!.classList.remove('ad-showing');
    page.document.querySelector('.title')!.textContent = 'Dai Dai';
    page.document.querySelector('.byline')!.textContent = 'Shakira and Burna Boy';
    page.document.title = 'Dai Dai - YouTube Music';
    await vi.advanceTimersByTimeAsync(500);
    expect(sendMessage).toHaveBeenCalledOnce();
    expect(drawer.querySelector('.notice-title')?.textContent).toBe('No lyrics found');

    page.setUrl('https://music.youtube.com/watch?v=lmnopqrstuv');
    await vi.advanceTimersByTimeAsync(500);
    expect(sendMessage.mock.calls.map(([request]) => request.payload)).toEqual([
      { videoId: 'abcdefghijk' }, { videoId: 'lmnopqrstuv' },
    ]);
    expect(drawer.querySelector('.title')?.textContent).toBe('Next song');
    ctx.invalidate();
  });
});
