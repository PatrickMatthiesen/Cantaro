import {
  extractCrunchyrollEpisodeMetadata,
  trackVideoProgress,
  type VideoProgressTracker,
} from '../lib/crunchyrollAdapter';
import type { MediaObservationMessage } from '../lib/mediaObservation';

export default defineContentScript({
  matches: ['https://www.crunchyroll.com/watch/*'],

  main() {
    console.log('Cantaro: Crunchyroll watch-state tracker loaded');
    startCrunchyrollWatchTracker();
  },
});

function startCrunchyrollWatchTracker(): void {
  let tracker: VideoProgressTracker | null = null;
  let currentWatchId: string | undefined;
  const submittedWatchIds = new Set<string>();

  const restartTracking = () => {
    const metadata = extractCrunchyrollEpisodeMetadata(document, location);
    const nextWatchId = metadata?.siteMediaId ?? location.href;

    if (nextWatchId === currentWatchId && tracker) {
      return;
    }

    tracker?.dispose();
    tracker = null;
    currentWatchId = nextWatchId;

    if (!metadata || submittedWatchIds.has(nextWatchId)) {
      return;
    }

    tracker = trackVideoProgress(document, metadata, (observation) => {
      submittedWatchIds.add(nextWatchId);

      const message: MediaObservationMessage = {
        type: 'MEDIA_OBSERVATION',
        payload: observation,
      };

      browser.runtime.sendMessage(message).catch((error: unknown) => {
        console.warn('Cantaro: failed to send media observation', error);
      });
    });
  };

  restartTracking();
  observePageChanges(restartTracking);
}

function observePageChanges(onChange: () => void): void {
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;
  let lastHref = location.href;

  const scheduleChange = () => {
    if (debounceTimer !== null) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
      debounceTimer = null;

      if (location.href !== lastHref) {
        lastHref = location.href;
      }

      if (location.pathname.startsWith('/watch/')) {
        onChange();
      }
    }, 500);
  };

  const titleEl = document.querySelector('title');
  if (titleEl) {
    new MutationObserver(scheduleChange).observe(titleEl, { childList: true });
  }

  new MutationObserver(scheduleChange).observe(document.documentElement, {
    childList: true,
    subtree: true,
  });
}
