import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import type { MediaWatchTabContext } from '../../../../contracts/mediaTabContext';
import type {
  ResolveWatchObservationRequest,
  WatchProgressObservation,
  WatchSubmissionResult,
} from '../../../../contracts/watchObservation';
import type { MessageResult } from '../../../../../../platform/messaging/messageResult';
import { createCorrelationId } from '../../../../../../platform/messaging/messageResult';
import {
  startMediaControllerRuntime,
} from '../../../runtime/contentControllerRuntime';
import {
  extractCrunchyrollWatchMetadata,
  extractEpisodeId,
  readSelectedPlayerTracks,
  trackVideoProgress,
  type CrunchyrollWatchMetadata,
  type VideoProgressTracker,
  type VideoProgressTrackerStatus,
} from './watchAdapter';
import {
  isFreshNavigationMetadata,
  watchMetadataFingerprint,
} from './navigationMetadata';
import { decideThresholdSubmission } from './thresholdSubmission';
import type { WatchResolutionOverlayHandle } from './watchResolutionOverlay';
import { extensionLogMessage } from '../../../../../../platform/diagnostics/extensionIdentity';

const RESTART_DELAY_MS = 500;

export interface WatchControllerDependencies {
  isTrackingPaused(): Promise<boolean>;
  watchTrackingPause(listener: () => void): () => void;
  submitWatch(
    observation: WatchProgressObservation,
    correlationId: string,
  ): Promise<MessageResult<WatchSubmissionResult>>;
  resolveWatch(
    request: ResolveWatchObservationRequest,
    correlationId: string,
  ): Promise<MessageResult<{ resolved: true }>>;
  showResolution(
    resolution: NonNullable<WatchSubmissionResult['resolution']>,
    resolve: (request: ResolveWatchObservationRequest) => Promise<void>,
  ): WatchResolutionOverlayHandle;
  readVerboseLogging(): Promise<boolean>;
  watchVerboseLogging(listener: (enabled: boolean) => void): () => void;
}

export interface WatchController {
  getSnapshot(): MediaWatchTabContext;
  requestRestart(): void;
  dispose(): void;
}

interface TrackingAttempt {
  metadata: CrunchyrollWatchMetadata;
  watchId: string;
}

export function createCrunchyrollWatchController(
  ctx: ContentScriptContext,
  dependencies: WatchControllerDependencies,
): WatchController {
  let tracker: VideoProgressTracker | null = null;
  let overlay: WatchResolutionOverlayHandle | null = null;
  let currentWatchId: string | undefined;
  let currentMetadataFingerprint: string | undefined;
  let currentMetadata: CrunchyrollWatchMetadata | undefined;
  let previousNavigationFingerprint: string | undefined;
  let restartTimer: number | null = null;
  let restartRunning = false;
  let restartRequested = false;
  let verboseLogging = false;
  let lastPlayerSelectionLogKey: string | undefined;
  const submittedWatchIds = new Set<string>();
  let snapshot = createInitialSnapshot();
  let notifyContextChanged: () => void = () => undefined;

  const updateSnapshot = (changes: Partial<MediaWatchTabContext>) => {
    snapshot = { ...snapshot, ...changes, pageUrl: location.href };
    notifyContextChanged();
  };

  const verboseLog = (message: string, details?: unknown) => {
    if (!verboseLogging) return;
    if (details === undefined) console.debug(extensionLogMessage(message));
    else console.debug(extensionLogMessage(message), details);
  };

  const requestRestart = () => {
    if (ctx.isInvalid) return;
    if (restartRunning) {
      restartRequested = true;
      return;
    }
    if (restartTimer !== null) clearTimeout(restartTimer);
    restartTimer = ctx.setTimeout(() => {
      restartTimer = null;
      void restartTracking();
    }, RESTART_DELAY_MS);
  };

  const restartTracking = async () => {
    if (ctx.isInvalid) return;
    if (restartRunning) {
      restartRequested = true;
      return;
    }

    restartRunning = true;
    restartRequested = false;
    try {
      await updateTracker();
    } catch (error) {
      handleTrackingFailure(error);
    } finally {
      restartRunning = false;
      if (restartRequested) requestRestart();
    }
  };

  const updateTracker = async () => {
    if (await dependencies.isTrackingPaused()) {
      tracker = disposeTracker(tracker);
      updateSnapshot({
        status: 'paused',
        message: 'Episode tracking is temporarily paused.',
      });
      return;
    }

    const attempt = prepareTrackingAttempt(
      currentWatchId,
      tracker,
      submittedWatchIds,
      previousNavigationFingerprint,
    );
    if (attempt) armTracker(attempt);
  };

  const armTracker = (attempt: TrackingAttempt) => {
    tracker = disposeTracker(tracker);
    overlay?.close();
    overlay = null;
    currentWatchId = attempt.watchId;
    currentMetadata = attempt.metadata;
    currentMetadataFingerprint = watchMetadataFingerprint(attempt.metadata);
    previousNavigationFingerprint = undefined;
    updateSnapshot({
      status: 'ready',
      providerEpisodeId: attempt.watchId,
      seriesTitle: attempt.metadata.seriesTitle,
      episodeTitle: attempt.metadata.episodeTitle,
      episodeNumber: attempt.metadata.episodeNumber,
      watchProgressPercent: undefined,
      message: undefined,
    });
    verboseLog('Cantaro: Crunchyroll watch tracker armed', {
      watchId: attempt.watchId,
      seriesTitle: attempt.metadata.seriesTitle,
      episodeTitle: attempt.metadata.episodeTitle,
    });
    tracker = trackVideoProgress(
      document,
      attempt.metadata,
      observation => handleThresholdReached(attempt.watchId, observation),
      { onStatus: updateProgress },
    );
  };

  const handleThresholdReached = (
    watchId: string,
    observation: WatchProgressObservation,
  ) => {
    void submitThresholdObservation(watchId, observation);
  };

  const submitThresholdObservation = async (
    watchId: string,
    observation: WatchProgressObservation,
  ) => {
    const decision = await decideThresholdSubmission(
      dependencies.isTrackingPaused,
      () => isCurrentWatchPage(ctx, watchId, currentWatchId, location.pathname),
    );
    if (decision === 'paused') {
      tracker = disposeTracker(tracker);
      updateSnapshot({
        status: 'paused',
        message: 'Episode tracking is temporarily paused.',
      });
      return;
    }
    if (decision === 'stale') return;
    submittedWatchIds.add(watchId);
    await submitWatchProgress(watchId, observation);
  };

  const handleTrackingFailure = (error: unknown) => {
    updateSnapshot({
      status: 'error',
      message: error instanceof Error ? error.message : 'Unexpected watch tracking failure.',
    });
    console.error(extensionLogMessage('Crunchyroll watch tracker failed'), {
      pageUrl: location.href,
      error,
    });
  };

  const updateProgress = (status: VideoProgressTrackerStatus) => {
    if (status.type === 'progress' || status.type === 'threshold-reached') {
      updateSnapshot({ watchProgressPercent: status.watchProgressPercent });
      if (status.type === 'threshold-reached') {
        updateSnapshot({ status: 'submitting' });
      }
      logWatchProgressStatus(status, currentWatchId, verboseLog);
      return;
    }
    if (status.type === 'progress-unavailable') {
      logWatchProgressStatus(status, currentWatchId, verboseLog);
    }
  };

  const submitWatchProgress = async (
    watchId: string,
    observation: WatchProgressObservation,
  ) => {
    const correlationId = createCorrelationId();
    verboseLog('Cantaro: submitting Crunchyroll watch progress', { correlationId, observation });
    try {
      const result = await dependencies.submitWatch(observation, correlationId);
      if (!isCurrentDelivery(ctx, watchId, currentWatchId)) return;
      if (!result.ok) {
        handleDeliveryFailure(watchId, correlationId, result.error);
        return;
      }
      applyWatchDelivery(result.value, correlationId);
    } catch (error) {
      handleDeliveryFailure(watchId, correlationId, error);
    }
  };

  const applyWatchDelivery = (
    result: WatchSubmissionResult,
    correlationId: string,
  ) => {
    verboseLog('Cantaro: Crunchyroll watch-progress delivery result', {
      correlationId,
      result,
    });
    updateSnapshot({
      status: result.status === 'queued' ? 'queued' : 'submitted',
      message: watchDeliveryMessage(result),
    });
    if (result.status !== 'pending_resolution' || !result.resolution) return;

    overlay?.close();
    overlay = dependencies.showResolution(
      result.resolution,
      request => resolveWatchProgress(request),
    );
  };

  const handleDeliveryFailure = (
    watchId: string,
    correlationId: string,
    error: unknown,
  ) => {
    submittedWatchIds.delete(watchId);
    updateSnapshot({
      status: 'error',
      message: deliveryErrorMessage(error),
    });
    console.warn(extensionLogMessage('Crunchyroll watch-progress delivery failed'), {
      correlationId,
      error,
    });
    requestRestart();
  };

  const resolveWatchProgress = async (request: ResolveWatchObservationRequest) => {
    const correlationId = createCorrelationId();
    const result = await dependencies.resolveWatch(request, correlationId);
    if (!result.ok) throw new Error(result.error.message);
    updateSnapshot({ status: 'submitted', message: undefined });
  };

  const observePlayerSelection = (selection: ReturnType<typeof readSelectedPlayerTracks>) => {
    if (!selection.audioLabel) return;
    const logKey = `${selection.audioLabel}|${selection.subtitleLabel ?? ''}|${selection.releaseTrack ?? ''}`;
    if (logKey === lastPlayerSelectionLogKey) return;
    lastPlayerSelectionLogKey = logKey;
    const metadata = currentMetadata;
    if (!selection.releaseTrack) {
      verboseLog('Cantaro: Crunchyroll release track selection not recognized', {
        watchId: currentWatchId,
        audioLabel: selection.audioLabel,
        subtitleLabel: selection.subtitleLabel,
      });
      return;
    }
    const changed = metadata?.releaseTrack !== selection.releaseTrack;
    if (metadata) {
      metadata.releaseTrack = selection.releaseTrack;
      metadata.nextEpisodeReleaseTrack = selection.releaseTrack;
    }
    verboseLog('Cantaro: selected Crunchyroll release track observed', {
      watchId: currentWatchId,
      releaseTrack: selection.releaseTrack,
      audioLabel: selection.audioLabel,
      subtitleLabel: selection.subtitleLabel,
      changed,
    });
  };

  const observer = new MutationObserver((mutations) => {
    observePlayerSelection(readSelectedPlayerTracks(document));
    if (mutations.some(hasRelevantPageChange)) requestRestart();
  });
  observer.observe(document.documentElement, { childList: true, subtree: true });
  const handlePlayerTrackSelection = (event: Event) => {
    const target = event.target instanceof Element
      ? event.target.closest('[role="menuitemradio"]')
      : null;
    if (!target) return;
    const menuLabel = target.closest('[role="menu"]')?.getAttribute('aria-label');
    if (menuLabel !== 'Audio Track Selection'
      && menuLabel !== 'Subtitle and closed caption selection') return;
    const selection = readSelectedPlayerTracks(document, target);
    lastPlayerSelectionLogKey = undefined;
    observePlayerSelection(selection);
  };
  ctx.addEventListener(document, 'click', handlePlayerTrackSelection, true);
  notifyContextChanged = startMediaControllerRuntime(ctx, dependencies, () => snapshot, (enabled) => {
    verboseLogging = enabled;
  }, () => {
    verboseLog('Cantaro: Crunchyroll watch controller started', { pageUrl: location.href });
    requestRestart();
  }, () => extractEpisodeId(location.pathname) !== undefined);

  const stopWatchingPause = dependencies.watchTrackingPause(() => {
    tracker = disposeTracker(tracker);
    requestRestart();
  });

  const handleLocationChange = () => {
    const nextWatchId = extractEpisodeId(location.pathname) ?? location.href;
    if (nextWatchId === currentWatchId) {
      requestRestart();
      return;
    }
    tracker = disposeTracker(tracker);
    overlay?.close();
    overlay = null;
    previousNavigationFingerprint = currentMetadataFingerprint;
    currentMetadataFingerprint = undefined;
    currentMetadata = undefined;
    lastPlayerSelectionLogKey = undefined;
    currentWatchId = undefined;
    updateSnapshot({
      status: 'starting',
      providerEpisodeId: nextWatchId,
      seriesTitle: undefined,
      episodeTitle: undefined,
      episodeNumber: undefined,
      watchProgressPercent: undefined,
      message: 'Waiting for the new Crunchyroll episode to render.',
    });
    requestRestart();
  };

  ctx.addEventListener(window, 'wxt:locationchange', handleLocationChange);
  ctx.onInvalidated(() => {
    observer.disconnect();
    stopWatchingPause();
    tracker = disposeTracker(tracker);
    overlay?.close();
    if (restartTimer !== null) clearTimeout(restartTimer);
  });

  return {
    getSnapshot: () => snapshot,
    requestRestart,
    dispose: () => ctx.abort(),
  };
}

export function logWatchProgressStatus(
  status: VideoProgressTrackerStatus,
  watchId: string | undefined,
  verboseLog: (message: string, details?: unknown) => void,
): void {
  const details = { watchId, ...status };
  if (status.type === 'threshold-reached') {
    console.info(extensionLogMessage('Crunchyroll watch threshold reached'), details);
    return;
  }
  if (status.type === 'progress') {
    verboseLog('Cantaro: Crunchyroll watch progress', details);
    return;
  }
  verboseLog('Cantaro: Crunchyroll video progress unavailable', details);
}

function createInitialSnapshot(): MediaWatchTabContext {
  return {
    feature: 'media',
    provider: 'crunchyroll',
    pageKind: 'watch',
    pageUrl: location.href,
    status: 'starting',
  };
}

function prepareTrackingAttempt(
  currentWatchId: string | undefined,
  tracker: VideoProgressTracker | null,
  submittedWatchIds: Set<string>,
  previousNavigationFingerprint: string | undefined,
): TrackingAttempt | null {
  const watchId = currentPageWatchId(location);
  if (!canPrepareTracking(watchId, currentWatchId, tracker, submittedWatchIds)) return null;
  const metadata = extractCrunchyrollWatchMetadata(document, location);
  return metadata && isFreshNavigationMetadata(previousNavigationFingerprint, metadata)
    ? { metadata, watchId }
    : null;
}

function currentPageWatchId(locationLike: Location): string {
  return extractEpisodeId(locationLike.pathname) ?? locationLike.href;
}

function canPrepareTracking(
  watchId: string,
  currentWatchId: string | undefined,
  tracker: VideoProgressTracker | null,
  submittedWatchIds: Set<string>,
): boolean {
  if (isAlreadyTracking(watchId, currentWatchId, tracker)) return false;
  if (submittedWatchIds.has(watchId)) return false;
  return hasVideo(document);
}

function isAlreadyTracking(
  watchId: string,
  currentWatchId: string | undefined,
  tracker: VideoProgressTracker | null,
): boolean {
  return watchId === currentWatchId && tracker !== null;
}

function hasVideo(doc: Document): boolean {
  return doc.querySelector('video') !== null;
}

function isCurrentDelivery(
  ctx: ContentScriptContext,
  watchId: string,
  currentWatchId: string | undefined,
): boolean {
  if (ctx.isInvalid) return false;
  return watchId === currentWatchId;
}

function isCurrentWatchPage(
  ctx: ContentScriptContext,
  watchId: string,
  currentWatchId: string | undefined,
  pathname: string,
): boolean {
  if (!isCurrentDelivery(ctx, watchId, currentWatchId)) return false;
  return extractEpisodeId(pathname) === watchId;
}

function watchDeliveryMessage(result: WatchSubmissionResult): string | undefined {
  if (result.status === 'queued') return 'Watch progress is queued for delivery.';
  if (result.status === 'pending_resolution') return 'Watch progress needs a media match.';
  return undefined;
}

function deliveryErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (error && typeof error === 'object' && 'message' in error) {
    const message = (error as { message?: unknown }).message;
    if (typeof message === 'string') return message;
  }
  return 'Watch progress could not be delivered.';
}

function disposeTracker(tracker: VideoProgressTracker | null): null {
  tracker?.dispose();
  return null;
}

const WATCH_METADATA_SELECTOR = [
  'video',
  'h1',
  'a[href*="/series/"]',
  '[data-t="series-title"]',
  '[data-t="show-title"]',
  '[data-t="episode-title"]',
  '[data-t="title"]',
  '[data-testid="series-title"]',
  '[data-testid="episode-title"]',
].join(', ');

export function hasRelevantPageChange(mutation: Pick<MutationRecord, 'addedNodes'>): boolean {
  return Array.from(mutation.addedNodes).some((node) => {
    if (!(node instanceof Element)) return false;
    return node.matches(WATCH_METADATA_SELECTOR)
      || Boolean(node.querySelector(WATCH_METADATA_SELECTOR));
  });
}
