import {
  extractCrunchyrollEpisodeMetadata,
  trackVideoProgress,
  type CrunchyrollEpisodeMetadata,
  type VideoProgressTracker,
} from '../lib/crunchyrollAdapter';
import type { MediaObservation, MediaObservationMessage } from '../lib/mediaObservation';

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

    if (isTrackingCurrentWatch(nextWatchId, currentWatchId, tracker)) {
      return;
    }

    tracker = disposeTracker(tracker);
    currentWatchId = nextWatchId;

    if (shouldSkipTracking(metadata, nextWatchId, submittedWatchIds)) {
      return;
    }

    tracker = trackVideoProgress(
      document,
      metadata,
      createObservationSubmitter(nextWatchId, submittedWatchIds),
    );
  };

  restartTracking();
  observePageChanges(restartTracking);
}

function isTrackingCurrentWatch(
  nextWatchId: string,
  currentWatchId: string | undefined,
  tracker: VideoProgressTracker | null,
): boolean {
  return nextWatchId === currentWatchId && tracker !== null;
}

function disposeTracker(tracker: VideoProgressTracker | null): null {
  tracker?.dispose();
  return null;
}

function shouldSkipTracking(
  metadata: CrunchyrollEpisodeMetadata | null,
  watchId: string,
  submittedWatchIds: Set<string>,
): metadata is null {
  return !metadata || submittedWatchIds.has(watchId);
}

function createObservationSubmitter(
  watchId: string,
  submittedWatchIds: Set<string>,
): (observation: MediaObservation) => void {
  return (observation) => {
    submittedWatchIds.add(watchId);
    submitObservation(observation);
  };
}

function submitObservation(observation: MediaObservation): void {
  const message: MediaObservationMessage = {
    type: 'MEDIA_OBSERVATION',
    payload: observation,
  };

  browser.runtime.sendMessage(message).catch((error: unknown) => {
    console.warn('Cantaro: failed to send media observation', error);
  });
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
