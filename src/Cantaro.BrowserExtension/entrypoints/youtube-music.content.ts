import { MUSIC_CONTEXT_STORAGE_KEY, parseYouTubeMusicContext } from '../lib/musicContext';

export default defineContentScript({
  matches: ['https://www.youtube.com/*', 'https://youtube.com/*', 'https://music.youtube.com/*'],
  main() {
    let previousKey = '';
    // DOM fallbacks intentionally cover both YouTube products and their evolving player shells.
    // fallow-ignore-next-line complexity
    const observe = async () => {
      const visibleTitle = document.querySelector<HTMLElement>('ytmusic-player-bar .title, h1.ytd-watch-metadata')?.innerText.trim();
      const visibleArtist = document.querySelector<HTMLElement>('ytmusic-player-bar .byline, #owner #channel-name')?.innerText.trim();
      const musicTitle = visibleTitle || document.querySelector<HTMLMetaElement>('meta[name="title"]')?.content;
      const artist = visibleArtist || document.querySelector<HTMLMetaElement>('meta[itemprop="author"]')?.content;
      const context = parseYouTubeMusicContext(location.href, musicTitle, artist);
      const key = context ? `${context.site}:${context.externalId}:${context.title ?? ''}` : `none:${location.href}`;
      if (key === previousKey) return;
      previousKey = key;
      await browser.storage.local.set({ [MUSIC_CONTEXT_STORAGE_KEY]: context });
    };
    void observe();
    const observer = new MutationObserver(() => void observe());
    observer.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener('yt-navigate-finish', observe);
    window.addEventListener('popstate', observe);
  },
});
