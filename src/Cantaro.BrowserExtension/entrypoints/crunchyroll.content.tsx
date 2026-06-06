import {
  extractEpisodeId,
  extractCrunchyrollEpisodeMetadata,
  trackVideoProgress,
  type CrunchyrollEpisodeMetadata,
  type VideoProgressTrackerStatus,
  type VideoProgressTracker,
} from '../lib/crunchyrollAdapter';
import { isEpisodeTrackingTimedOut } from '../lib/episodeTrackingTimeout';
import { MediaResolutionPicker } from '../lib/MediaResolutionPicker';
import type {
  MediaObservation,
  MediaObservationMessage,
  ResolveMediaObservationRequest,
  SubmitMediaObservationResponse,
} from '../lib/mediaObservation';
import React from 'react';
import ReactDOM from 'react-dom/client';

const TRACKER_DISPOSE_KEY = '__cantaroCrunchyrollWatchTrackerDispose';

interface TrackingAttempt {
  metadata: CrunchyrollEpisodeMetadata;
  watchId: string;
}

declare global {
  interface Window {
    [TRACKER_DISPOSE_KEY]?: () => void;
  }
}

export default defineContentScript({
  matches: ['https://www.crunchyroll.com/watch/*'],

  main() {
    console.log('Cantaro: Crunchyroll watch-state tracker loaded');
    window[TRACKER_DISPOSE_KEY]?.();
    window[TRACKER_DISPOSE_KEY] = startCrunchyrollWatchTracker();
    registerResolutionOverlayListener();
  },
});

function startCrunchyrollWatchTracker(): () => void {
  let tracker: VideoProgressTracker | null = null;
  let currentWatchId: string | undefined;
  const submittedWatchIds = new Set<string>();
  const progressLogger = createProgressLogger(() => currentWatchId);

  const restartTracking = async () => {
    if (await isEpisodeTrackingTimedOut()) {
      tracker = disposeTracker(tracker);
      return;
    }

    const attempt = prepareTrackingAttempt(currentWatchId, tracker, submittedWatchIds);
    if (!attempt) return;

    progressLogger.reset();
    tracker = disposeTracker(tracker);
    currentWatchId = attempt.watchId;
    logTrackerArmed(attempt);

    tracker = trackVideoProgress(
      document,
      attempt.metadata,
      createObservationSubmitter(attempt.watchId, submittedWatchIds),
      { onStatus: progressLogger.log },
    );
  };

  void restartTracking();
  const stopObserving = observePageChanges(() => void restartTracking());

  return () => {
    tracker = disposeTracker(tracker);
    stopObserving();
  };
}

function prepareTrackingAttempt(
  currentWatchId: string | undefined,
  tracker: VideoProgressTracker | null,
  submittedWatchIds: Set<string>,
): TrackingAttempt | null {
  const watchId = extractEpisodeId(location.pathname) ?? location.href;

  if (isTrackingCurrentWatch(watchId, currentWatchId, tracker)) return null;
  if (!hasVideoCandidate(document)) {
    console.log('Cantaro: Crunchyroll tracker waiting for video element', { watchId });
    return null;
  }

  const metadata = extractCrunchyrollEpisodeMetadata(document, location);
  if (isTrackingCurrentWatch(watchId, currentWatchId, tracker)) return null;
  if (shouldSkipTracking(metadata, watchId, submittedWatchIds)) {
    logTrackerSkipped(watchId, metadata);
    return null;
  }

  return { metadata, watchId };
}

function logTrackerArmed(attempt: TrackingAttempt): void {
  console.log('Cantaro: Crunchyroll tracker armed', {
    watchId: attempt.watchId,
    titleText: attempt.metadata.titleText,
    episodeNumber: attempt.metadata.episodeNumber,
  });
}

function logTrackerSkipped(watchId: string, metadata: CrunchyrollEpisodeMetadata | null): void {
  console.log('Cantaro: Crunchyroll tracker skipped', {
    watchId,
    reason: metadata ? 'already-submitted' : 'metadata-unavailable',
  });
}

function createProgressLogger(getWatchId: () => string | undefined): { log(status: VideoProgressTrackerStatus): void; reset(): void } {
  let lastLoggedProgressBucket: number | null = null;
  let loggedUnavailableProgress = false;

  return {
    log(status) {
      if (status.type === 'progress-unavailable') {
        logUnavailableProgress(getWatchId(), status.currentTime, status.duration, loggedUnavailableProgress);
        loggedUnavailableProgress = true;
        return;
      }

      if (status.type === 'progress') {
        lastLoggedProgressBucket = logProgressBucket(getWatchId(), status, lastLoggedProgressBucket);
        return;
      }

      if (status.type === 'threshold-reached') {
        logThresholdReached(getWatchId(), status);
      }
    },
    reset() {
      lastLoggedProgressBucket = null;
      loggedUnavailableProgress = false;
    },
  };
}

function logUnavailableProgress(
  watchId: string | undefined,
  currentTime: number,
  duration: number,
  alreadyLogged: boolean,
): void {
  if (alreadyLogged) return;

  console.log('Cantaro: Crunchyroll video progress unavailable', {
    watchId,
    currentTime,
    duration,
  });
}

function logProgressBucket(
  watchId: string | undefined,
  status: { watchProgressPercent: number; positionSeconds: number; durationSeconds: number },
  lastLoggedProgressBucket: number | null,
): number | null {
  const bucket = Math.floor(status.watchProgressPercent / 10) * 10;
  if (bucket === lastLoggedProgressBucket) return lastLoggedProgressBucket;

  console.log('Cantaro: Crunchyroll watch progress', {
    watchId,
    watchProgressPercent: status.watchProgressPercent,
    positionSeconds: status.positionSeconds,
    durationSeconds: status.durationSeconds,
  });

  return bucket;
}

function logThresholdReached(
  watchId: string | undefined,
  status: { watchProgressPercent: number; positionSeconds: number; durationSeconds: number },
): void {
  console.log('Cantaro: Crunchyroll watch threshold reached', {
    watchId,
    watchProgressPercent: status.watchProgressPercent,
    positionSeconds: status.positionSeconds,
    durationSeconds: status.durationSeconds,
  });
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
    void submitObservation(observation);
  };
}

async function submitObservation(observation: MediaObservation): Promise<void> {
  if (await isEpisodeTrackingTimedOut()) {
    console.log('Cantaro: episode tracking is temporarily disabled');
    return;
  }

  const message: MediaObservationMessage = {
    type: 'MEDIA_OBSERVATION',
    payload: observation,
  };

  console.log('Cantaro: sending media observation to background', summarizeObservation(observation));

  browser.runtime.sendMessage(message).catch((error: unknown) => {
    console.warn('Cantaro: failed to send media observation', error);
  });
}

function registerResolutionOverlayListener(): void {
  browser.runtime.onMessage.addListener((message: MediaObservationMessage) => {
    if (message.type === 'SHOW_MEDIA_RESOLUTION') {
      showResolutionOverlay(message.payload);
    }
  });
}

function showResolutionOverlay(response: SubmitMediaObservationResponse): void {
  const container = ensureOverlayContainer();
  const root = ReactDOM.createRoot(container);
  const close = () => {
    root.unmount();
    container.remove();
  };

  root.render(
    <React.StrictMode>
      <ResolutionOverlay response={response} onClose={close} />
    </React.StrictMode>,
  );
}

function ResolutionOverlay({
  response,
  onClose,
}: {
  response: SubmitMediaObservationResponse;
  onClose: () => void;
}) {
  const [error, setError] = React.useState<string | null>(null);
  const [resolving, setResolving] = React.useState(false);

  const resolve = async (observationId: string, request: ResolveMediaObservationRequest) => {
    setResolving(true);
    setError(null);
    try {
      await browser.runtime.sendMessage({
        type: 'RESOLVE_MEDIA_OBSERVATION',
        payload: { observationId, request },
      } satisfies MediaObservationMessage);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to resolve episode.');
    } finally {
      setResolving(false);
    }
  };

  return (
    <div style={overlayBackdropStyle}>
      <MediaResolutionPicker
        response={response}
        surface="overlay"
        resolving={resolving}
        error={error}
        onResolve={resolve}
        onClose={onClose}
      />
    </div>
  );
}

function ensureOverlayContainer(): HTMLDivElement {
  document.getElementById('cantaro-resolution-overlay')?.remove();
  const container = document.createElement('div');
  container.id = 'cantaro-resolution-overlay';
  Object.assign(container.style, {
    position: 'fixed',
    inset: '0',
    zIndex: '2147483647',
    display: 'grid',
    placeItems: 'center',
    pointerEvents: 'auto',
  });
  document.documentElement.appendChild(container);
  return container;
}

const overlayBackdropStyle = {
  position: 'fixed',
  inset: 0,
  display: 'grid',
  placeItems: 'center',
  background: 'rgba(15, 23, 42, 0.36)',
  backdropFilter: 'blur(3px)',
  padding: 16,
} satisfies React.CSSProperties;

function summarizeObservation(observation: MediaObservation): Record<string, unknown> {
  return {
    siteId: observation.siteId,
    siteMediaId: observation.siteMediaId,
    titleText: observation.titleText,
    episodeNumber: observation.episodeNumber,
    watchProgressPercent: observation.watchProgressPercent,
    positionSeconds: observation.positionSeconds,
    durationSeconds: observation.durationSeconds,
  };
}

function hasVideoCandidate(doc: Document): boolean {
  return doc.querySelector('video') !== null;
}

function observePageChanges(onChange: () => void): () => void {
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;
  let lastHref = location.href;
  const observers: MutationObserver[] = [];

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
    const titleObserver = new MutationObserver(scheduleChange);
    titleObserver.observe(titleEl, { childList: true, characterData: true, subtree: true });
    observers.push(titleObserver);
  }

  const documentObserver = new MutationObserver((mutations) => {
    if (location.href !== lastHref || mutations.some(hasRelevantPageChange)) {
      scheduleChange();
    }
  });
  documentObserver.observe(document.documentElement, {
    childList: true,
    subtree: true,
  });
  observers.push(documentObserver);

  return () => {
    if (debounceTimer !== null) clearTimeout(debounceTimer);
    for (const observer of observers) {
      observer.disconnect();
    }
  };
}

function hasRelevantPageChange(mutation: MutationRecord): boolean {
  return Array.from(mutation.addedNodes).some(isRelevantAddedNode);
}

function isRelevantAddedNode(node: Node): boolean {
  if (node.nodeName.toLowerCase() === 'video') return true;
  if (!(node instanceof Element)) return false;

  return Boolean(node.querySelector('video, [data-t="series-title"], [data-t="episode-title"], [data-t="title"], [data-testid="series-title"], [data-testid="episode-title"]'));
}
