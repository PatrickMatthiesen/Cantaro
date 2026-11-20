import { parseHTML } from 'linkedom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import wistoriaWatchHtml from './fixtures/wistoria-season-2-episode-24.html?raw';

vi.mock('../../../../../../platform/messaging/tabContext', () => ({
  registerTabContext: () => () => undefined,
}));

import { createCrunchyrollWatchController } from './watchController';

function createContext(): ContentScriptContext {
  const invalidationCallbacks: Array<() => void> = [];
  return {
    isInvalid: false,
    setTimeout: (callback: () => void, delay: number) => window.setTimeout(callback, delay),
    addEventListener: () => undefined,
    onInvalidated: (callback: () => void) => invalidationCallbacks.push(callback),
    abort: () => invalidationCallbacks.forEach(callback => callback()),
  } as unknown as ContentScriptContext;
}

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function setVideoProgress(video: HTMLVideoElement, currentTime: number) {
  Object.defineProperties(video, {
    currentTime: { value: currentTime, writable: true },
    duration: { value: 100 },
    paused: { value: false },
  });
}

it.each(['atomic', 'staggered', 'pending threshold'])(
  'tracks the replacement player and submits once after a %s swap',
  async swap => {
    vi.useFakeTimers();
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    const page = parseHTML(wistoriaWatchHtml);
    for (const element of page.document.querySelectorAll('*')) {
      Object.defineProperty(element, 'getBoundingClientRect', { value: () => ({ width: 400, height: 28 }) });
    }
    vi.stubGlobal('window', page.window);
    vi.stubGlobal('document', page.document);
    vi.stubGlobal('location', new URL('https://www.crunchyroll.com/watch/GE00340382ENUS/a-story-of-a-dream-with-no-end'));
    vi.stubGlobal('MutationObserver', page.window.MutationObserver);
    vi.stubGlobal('Element', page.window.Element);
    // Snowfall's signed-in watch page places the video in the top document.
    const player = page.document.createElement('div');
    player.id = 'player-container';
    player.innerHTML = '<div class="bitmovinplayer-container aspect-16x9"><video playsinline></video></div>';
    page.document.querySelector('video')!.replaceWith(player);
    const oldVideo = player.querySelector('video')!;
    setVideoProgress(oldVideo, 40);
    const submitWatch = vi.fn<Parameters<typeof createCrunchyrollWatchController>[1]['submitWatch']>()
      .mockResolvedValue({ ok: true, correlationId: '', value: { status: 'accepted' } });
    const isTrackingPaused = vi.fn(async () => false);
    const controller = createCrunchyrollWatchController(createContext(), {
      isTrackingPaused,
      watchTrackingPause: () => () => undefined,
      readVerboseLogging: async () => false,
      watchVerboseLogging: () => () => undefined,
      readCollectionConsent: async () => true,
      watchCollectionConsent: () => () => undefined,
      readCatalogCollectionConsent: async () => false,
      watchCatalogCollectionConsent: () => () => undefined,
      submitWatch,
      resolveWatch: async () => ({ ok: true, correlationId: '', value: { resolved: true } }),
      showResolution: () => ({ close: () => undefined }),
    });

    await vi.advanceTimersByTimeAsync(500);
    expect(controller.getSnapshot()).toMatchObject({ status: 'ready', watchProgressPercent: 40 });

    const replacement = page.document.createElement('video');
    setVideoProgress(replacement, 60);
    let finishPauseCheck: ((paused: boolean) => void) | undefined;
    if (swap === 'pending threshold') {
      isTrackingPaused.mockReturnValueOnce(new Promise(resolve => { finishPauseCheck = resolve; }));
      oldVideo.currentTime = 86;
      oldVideo.dispatchEvent(new page.window.Event('timeupdate'));
      expect(controller.getSnapshot().status).toBe('submitting');
    }
    if (swap === 'staggered') {
      oldVideo.after(replacement);
      await vi.advanceTimersByTimeAsync(500);
      expect(controller.getSnapshot().watchProgressPercent).toBe(40);
      oldVideo.remove();
    } else {
      oldVideo.replaceWith(replacement);
    }
    oldVideo.currentTime = 100;
    oldVideo.dispatchEvent(new page.window.Event('ended'));
    for (let elapsed = 0; elapsed < 500; elapsed += 100) {
      await vi.advanceTimersByTimeAsync(100);
      page.document.body.append(page.document.createTextNode('Unrelated update'));
    }
    expect(submitWatch).not.toHaveBeenCalled();
    expect(controller.getSnapshot()).toMatchObject({ status: 'ready', watchProgressPercent: 60 });

    oldVideo.dispatchEvent(new page.window.Event('ended'));
    replacement.currentTime = 84;
    replacement.dispatchEvent(new page.window.Event('timeupdate'));
    expect(submitWatch).not.toHaveBeenCalled();
    expect(controller.getSnapshot().watchProgressPercent).toBe(84);

    replacement.currentTime = 85;
    replacement.dispatchEvent(new page.window.Event('timeupdate'));
    replacement.dispatchEvent(new page.window.Event('ended'));
    await vi.advanceTimersByTimeAsync(0);
    expect(submitWatch).toHaveBeenCalledOnce();
    expect(submitWatch).toHaveBeenCalledWith(expect.objectContaining({
      providerEpisodeId: 'GE00340382ENUS', episodeNumber: 24, watchProgressPercent: 85,
      positionSeconds: 85, durationSeconds: 100,
    }), expect.any(String));
    finishPauseCheck?.(false);
    await vi.advanceTimersByTimeAsync(0);
    expect(submitWatch).toHaveBeenCalledOnce();
    expect(controller.getSnapshot().status).toBe('submitted');
    controller.dispose();
  },
);

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
