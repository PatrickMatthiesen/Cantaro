import { lyricsSearchHints } from './searchHints';
import { getRuntimeConsentStatus, subscribeRuntimeConsent } from '../../../platform/consent/runtimeConsentClient';
import type { YouTubeLyricsDrawer } from './drawer';
import { mountYouTubeLyricsDrawer } from './drawer';
import { requestYouTubeLyrics, youtubeVideoId } from './youtubeLyrics';

interface ContentContext {
  onInvalidated(callback: () => void): void;
}

export function startYouTubeLyrics(ctx: ContentContext): void {
  let disposed = false;
  let consentRevision = 0;
  let enabled = false;
  let activeVideoId: string | null = null;
  let drawer: YouTubeLyricsDrawer | null = null;
  let hostObserver: MutationObserver | null = null;
  let locationTimer: ReturnType<typeof setInterval> | null = null;

  const stopPageListeners = () => {
    if (locationTimer === null) return;
    clearInterval(locationTimer);
    locationTimer = null;
  };

  const stopHostObserver = () => {
    hostObserver?.disconnect();
    hostObserver = null;
  };

  const removeDrawer = () => {
    stopHostObserver();
    drawer?.dispose();
    drawer = null;
    activeVideoId = null;
  };

  const syncLocation = () => {
    if (disposed || !enabled) return;
    // Read page URL only after the authenticated lyrics-consent gate opens.
    const nextVideoId = youtubeVideoId(window.location.href);
    if (!nextVideoId) {
      removeDrawer();
      return;
    }
    if (!drawer) {
      activeVideoId = nextVideoId;
      drawer = mountYouTubeLyricsDrawer({
        document,
        getCurrentVideoId: () => activeVideoId,
        requestLyrics: requestYouTubeLyrics,
        getSearchHints: () => lyricsSearchHints(document.title),
      });
      hostObserver = new MutationObserver(() => {
        const host = document.querySelector('[data-cantaro-youtube-lyrics]');
        if (!host && drawer && document.documentElement) {
          // Reattach only our host. This observer does not inspect the page subtree.
          const replacement = mountYouTubeLyricsDrawer({
            document,
            getCurrentVideoId: () => activeVideoId,
            requestLyrics: requestYouTubeLyrics,
            getSearchHints: () => lyricsSearchHints(document.title),
          });
          drawer.dispose();
          drawer = replacement;
        }
      });
      hostObserver.observe(document.documentElement, { childList: true });
      return;
    }
    if (activeVideoId !== nextVideoId) {
      activeVideoId = nextVideoId;
      drawer.refreshForCurrentVideo();
    }
  };

  function handleLocationChange() {
    syncLocation();
  }

  const setConsent = (isAllowed: boolean) => {
    if (disposed || enabled === isAllowed) return;
    enabled = isAllowed;
    if (enabled) {
      if (locationTimer === null) locationTimer = setInterval(handleLocationChange, 500);
      syncLocation();
    } else {
      removeDrawer();
      stopPageListeners();
    }
  };

  const unsubscribe = subscribeRuntimeConsent(status => {
    ++consentRevision;
    setConsent(status.authenticated && status.musicLyricsAllowed);
  });
  ctx.onInvalidated(() => {
    disposed = true;
    unsubscribe();
    removeDrawer();
    stopPageListeners();
  });

  const initialRevision = consentRevision;
  void getRuntimeConsentStatus().then(status => {
    if (disposed || consentRevision !== initialRevision) return;
    setConsent(status.authenticated && status.musicLyricsAllowed);
  }).catch(() => setConsent(false));
}
