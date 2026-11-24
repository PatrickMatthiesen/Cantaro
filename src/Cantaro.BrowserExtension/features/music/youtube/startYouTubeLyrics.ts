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
  let locationTimer: ReturnType<typeof setInterval> | null = null;

  const stopPageListeners = () => {
    if (locationTimer === null) return;
    clearInterval(locationTimer);
    locationTimer = null;
  };

  const removeDrawer = () => {
    drawer?.dispose();
    drawer = null;
    activeVideoId = null;
  };

  const syncLocation = () => {
    if (disposed || !enabled) return;
    // Read page URL only after the authenticated lyrics-consent gate opens.
    const url = window.location.href;
    const nextVideoId = youtubeVideoId(url);
    if (!nextVideoId) {
      removeDrawer();
      return;
    }
    const isMusic = new URL(url).hostname === 'music.youtube.com';
    const container = isMusic ? document.querySelector('ytmusic-player-page #side-panel') : document.body;
    if (!drawer) {
      if (!container) return;
      activeVideoId = nextVideoId;
      drawer = mountYouTubeLyricsDrawer({
        document,
        container,
        floating: !isMusic,
        getCurrentVideoId: () => activeVideoId,
        requestLyrics: requestYouTubeLyrics,
        // YouTube Music's page title may describe an ad.
        getSearchHints: isMusic ? undefined : () => lyricsSearchHints(document.title),
      });
      return;
    }
    drawer.placeIn(container);
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
