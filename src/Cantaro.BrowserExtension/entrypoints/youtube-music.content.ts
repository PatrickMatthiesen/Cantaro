import { MUSIC_CONTEXT_STORAGE_KEY, parseYouTubeMusicContext, type BrowserMusicContext } from '../lib/musicContext';
import { getCanonicalTrackLyrics } from '../lib/lyrics';
import { recognizeActiveYouTube } from '../lib/musicLibrary';
import { findLyricsPanelMount, createLyricsPanel, shouldRenderLyricsPanel, shouldResetLyricsPanel, type LyricsPanelRenderInput } from '../lib/youtubeLyricsPanel';

function readPageMusicContext(): BrowserMusicContext | null {
  const visibleTitle = document.querySelector<HTMLElement>('ytmusic-player-bar .title, h1.ytd-watch-metadata')?.innerText.trim();
  const visibleArtist = document.querySelector<HTMLElement>('ytmusic-player-bar .byline, #owner #channel-name')?.innerText.trim();
  return parseYouTubeMusicContext(
    location.href,
    visibleTitle || document.querySelector<HTMLMetaElement>('meta[name="title"]')?.content,
    visibleArtist || document.querySelector<HTMLMetaElement>('meta[itemprop="author"]')?.content,
  );
}

function lyricsContextKey(context: BrowserMusicContext): string {
  return `${context.site}:${context.externalId}:${context.title ?? ''}:${context.artist ?? ''}`;
}

function songLabel(context: BrowserMusicContext, title?: string, artist?: string): string {
  return [title || context.title, artist || context.artist].filter(Boolean).join(' · ') || 'Current song';
}

async function requestLyricsPanelRender(context: BrowserMusicContext, signal: AbortSignal, theme?: 'light' | 'dark'): Promise<LyricsPanelRenderInput | null> {
  const recognition = await recognizeActiveYouTube(context, signal);
  if (!shouldRenderLyricsPanel(true, recognition?.trackId, recognition?.classification)) return null;
  const trackLabel = songLabel(context, recognition.title, recognition.artist);
  const result = await getCanonicalTrackLyrics(recognition.trackId!, signal);
  return { trackLabel, theme, result };
}

export default defineContentScript({
  matches: ['https://www.youtube.com/*', 'https://youtube.com/*', 'https://music.youtube.com/*'],
  main() {
    let previousKey = '';
    let lyricsKey = '';
    let lyricsRequest = 0;
    let lyricsAbort: AbortController | null = null;
    let refreshTimeout: number | null = null;
    let lastLyricsRender: LyricsPanelRenderInput | null = null;
    let lyricsPanel = createLyricsPanel(document);

    // DOM fallbacks intentionally cover both YouTube products and their evolving player shells.
    // fallow-ignore-next-line complexity
    const observe = async () => {
      const context = readPageMusicContext();
      const key = context ? `${context.site}:${context.externalId}:${context.title ?? ''}` : `none:${location.href}`;
      if (key === previousKey) return;
      if (shouldResetLyricsPanel(previousKey, key)) resetLyricsPanel();
      previousKey = key;
      await browser.storage.local.set({ [MUSIC_CONTEXT_STORAGE_KEY]: context });
      queueLyricsRefresh();
    };

    const resetLyricsPanel = () => {
      lyricsAbort?.abort();
      lyricsAbort = null;
      lyricsRequest += 1;
      lyricsKey = '';
      lastLyricsRender = null;
      lyricsPanel.destroy();
      lyricsPanel = createLyricsPanel(document);
    };

    // This coordinates navigation, cancellation, and a stale-response guard in one place.
    // fallow-ignore-next-line complexity
    const refreshLyrics = async () => {
      const context = readPageMusicContext();
      const settings = await browser.storage.local.get(['injectLyricsOnYouTube', 'cantaroTheme']);
      if (!settings.injectLyricsOnYouTube || !context) {
        resetLyricsPanel();
        return;
      }

      const key = lyricsContextKey(context);
      const mount = findLyricsPanelMount(document, context.site);
      if (key === lyricsKey && lastLyricsRender) {
        lyricsPanel.mount(mount);
        lyricsPanel.render(lastLyricsRender);
        return;
      }

      lyricsAbort?.abort();
      const controller = new AbortController();
      lyricsAbort = controller;
      const request = ++lyricsRequest;
      lyricsKey = key;

      try {
        const theme = settings.cantaroTheme === 'light' || settings.cantaroTheme === 'dark' ? settings.cantaroTheme : undefined;
        const render = await requestLyricsPanelRender(context, controller.signal, theme);
        if (request !== lyricsRequest || controller.signal.aborted) return;
        if (!render) {
          resetLyricsPanel();
          return;
        }
        lastLyricsRender = render;
        lyricsPanel.mount(findLyricsPanelMount(document, context.site));
        lyricsPanel.render(lastLyricsRender);
      } catch (error) {
        if (controller.signal.aborted || request !== lyricsRequest) return;
        lastLyricsRender = {
          trackLabel: songLabel(context),
          error: error instanceof Error ? error.message : 'Lyrics could not be loaded right now.',
        };
        lyricsPanel.mount(findLyricsPanelMount(document, context.site));
        lyricsPanel.render(lastLyricsRender);
      }
    };

    const queueLyricsRefresh = () => {
      if (refreshTimeout !== null) window.clearTimeout(refreshTimeout);
      refreshTimeout = window.setTimeout(() => {
        refreshTimeout = null;
        void refreshLyrics();
      }, 180);
    };

    const restoreLyricsPanel = () => {
      if (!lastLyricsRender) return;
      const context = readPageMusicContext();
      if (context) lyricsPanel.mount(findLyricsPanelMount(document, context.site));
    };

    void observe();
    const observer = new MutationObserver(() => void observe());
    observer.observe(document.documentElement, { childList: true, subtree: true });
    const mutationObserver = new MutationObserver(() => {
      if (refreshTimeout !== null) return;
      refreshTimeout = window.setTimeout(() => {
        refreshTimeout = null;
        restoreLyricsPanel();
      }, 600);
    });
    mutationObserver.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener('yt-navigate-finish', observe);
    window.addEventListener('popstate', observe);
    const onStorageChanged = (changes: Record<string, browser.storage.StorageChange>, areaName: string) => {
      if (areaName === 'local' && (changes.injectLyricsOnYouTube || changes.cantaroTheme)) queueLyricsRefresh();
    };
    browser.storage.onChanged.addListener(onStorageChanged);

    return () => {
      observer.disconnect();
      mutationObserver.disconnect();
      lyricsAbort?.abort();
      if (refreshTimeout !== null) window.clearTimeout(refreshTimeout);
      browser.storage.onChanged.removeListener(onStorageChanged);
      lyricsPanel.destroy();
    };
  },
});
