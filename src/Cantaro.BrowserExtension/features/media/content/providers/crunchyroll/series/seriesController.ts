import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import type {
  CatalogSubmissionResult,
  SeriesCatalogObservation,
} from '../../../../contracts/catalogObservation';
import type { MediaSeriesTabContext } from '../../../../contracts/mediaTabContext';
import type { MessageResult } from '../../../../../../platform/messaging/messageResult';
import { createCorrelationId } from '../../../../../../platform/messaging/messageResult';
import {
  startMediaControllerRuntime,
  logContentVerbose,
} from '../../../runtime/contentControllerRuntime';
import {
  createContentCollectionGate,
  createContentCollectionStateHandler,
  updateContentSnapshot,
  type ContentCollectionConsentDependencies,
  type ContentCollectionGate,
} from '../../../runtime/contentCollectionGate';
import {
  inspectSeriesPage,
  catalogObservationFingerprint,
  type SeriesExtractionDiagnostics,
  type SeriesExtractionIssue,
} from './seriesParser';
import { buildSeriesDiscoveryLog } from './seriesLogging';
import { extractSeriesId } from '../shared/crunchyrollUrls';
import { stripUrlQueryAndFragment } from '../../../../contracts/observationUrl';
import { extensionLogMessage } from '../../../../../../platform/diagnostics/extensionIdentity';
import { sanitizeDiagnosticDetails } from '../../../../../../platform/diagnostics/logger';

const SCAN_DELAY_MS = 750;
const FAILURE_WARNING_DELAY_MS = 5_000;

export interface SeriesControllerDependencies extends ContentCollectionConsentDependencies {
  submitCatalog(
    observation: SeriesCatalogObservation,
    correlationId: string,
  ): Promise<MessageResult<CatalogSubmissionResult>>;
  readVerboseLogging(): Promise<boolean>;
  watchVerboseLogging(listener: (enabled: boolean) => void): () => void;
  now?(): number;
}

export interface SeriesController {
  getSnapshot(): MediaSeriesTabContext;
  requestScan(): void;
  dispose(): void;
}

interface FailureState {
  fingerprint: string;
  firstSeenAt: number;
  warned: boolean;
}

export function createCrunchyrollSeriesController(
  ctx: ContentScriptContext,
  dependencies: SeriesControllerDependencies,
): SeriesController {
  let verboseLogging = false;
  let scanTimer: number | null = null;
  let scanRunning = false;
  let scanRequested = false;
  let lastSubmittedFingerprint = '';
  let lastLoggedFingerprint = '';
  let failure: FailureState | null = null;
  let notifyContextChanged: () => void = () => undefined;
  let collectionAllowed = false;
  let collectionGate: ContentCollectionGate;
  let snapshot = createInitialSnapshot(collectionAllowed);

  const updateSnapshot = (changes: Partial<MediaSeriesTabContext>) => {
    snapshot = updateContentSnapshot(snapshot, changes, collectionAllowed);
    notifyContextChanged();
  };

  const logVerbose = (message: string, details?: unknown) => {
    logContentVerbose(verboseLogging, message, details);
  };

  const requestScan = () => {
    if (ctx.isInvalid) return;
    if (!collectionAllowed) return;
    if (scanRunning) {
      scanRequested = true;
      return;
    }
    if (scanTimer !== null) clearTimeout(scanTimer);
    scanTimer = ctx.setTimeout(() => {
      scanTimer = null;
      void runScan();
    }, SCAN_DELAY_MS);
  };

  const runScan = async () => {
    if (ctx.isInvalid) return;
    if (scanRunning) {
      scanRequested = true;
      return;
    }

    scanRunning = true;
    scanRequested = false;
    try {
      if (isCurrentSeriesPage()) await scanPage();
    } catch (error) {
      handleUnexpectedFailure(error);
    } finally {
      scanRunning = false;
      if (scanRequested) requestScan();
    }
  };

  const scanPage = async () => {
    if (!collectionAllowed) return;
    const generation = collectionGate.getState().generation;
    const extraction = inspectSeriesPage(document, location);
    logVerbose('Cantaro: Crunchyroll series scan', extraction.diagnostics);
    if (!extraction.observation) {
      handleExtractionFailure(extraction.diagnostics);
      return;
    }
    if (!collectionAllowed || generation !== collectionGate.getState().generation) return;
    await processObservation(extraction.observation, generation);
  };

  const processObservation = async (observation: SeriesCatalogObservation, generation: number) => {
    if (!collectionAllowed || generation !== collectionGate.getState().generation) return;
    failure = null;
    updateSnapshot({
      status: 'ready',
      providerSeriesId: observation.providerSeriesId,
      seriesTitle: observation.seriesTitle,
      seasonTitle: observation.seasonTitle,
      observedEpisodeCount: observation.episodes.length,
      message: undefined,
    });

    const fingerprint = catalogObservationFingerprint(observation);
    if (verboseLogging && fingerprint !== lastLoggedFingerprint) {
      console.debug(
        extensionLogMessage('found Crunchyroll episodes'),
        buildSeriesDiscoveryLog(observation),
      );
      lastLoggedFingerprint = fingerprint;
    }
    if (fingerprint === lastSubmittedFingerprint) {
      logVerbose('Cantaro: unchanged Crunchyroll catalog snapshot skipped', { fingerprint });
      return;
    }
    await submitObservation(observation, fingerprint, generation);
  };

  const submitObservation = async (
    observation: SeriesCatalogObservation,
    fingerprint: string,
    generation: number,
  ) => {
    if (!collectionAllowed || generation !== collectionGate.getState().generation) return;
    const correlationId = createCorrelationId();
    updateSnapshot({ status: 'submitting', submissionStatus: undefined });
    logVerbose('Cantaro: submitting Crunchyroll catalog snapshot', {
      correlationId,
      providerSeriesId: observation.providerSeriesId,
      seasonTitle: observation.seasonTitle,
      episodeCount: observation.episodes.length,
      episodes: observation.episodes.map(episode => ({
        providerEpisodeId: episode.providerEpisodeId,
        episodeNumber: episode.episodeNumber,
        releaseTrack: episode.releaseTrack,
      })),
    });
    const result = await dependencies.submitCatalog(observation, correlationId);
    if (ctx.isInvalid || !collectionAllowed || generation !== collectionGate.getState().generation) return;
    applyDeliveryResult(result, correlationId);
    if (isDeliveredCatalog(result)) lastSubmittedFingerprint = fingerprint;
  };

  const handleUnexpectedFailure = (error: unknown) => {
    if (!collectionAllowed) return;
    updateSnapshot({
      status: 'error',
      message: error instanceof Error ? error.message : 'Unexpected series collection failure.',
    });
    console.error(extensionLogMessage('Crunchyroll series collection failed'), {
      pageUrl: stripUrlQueryAndFragment(location.href),
      error: sanitizeDiagnosticDetails(error),
    });
  };

  const handleExtractionFailure = (diagnostics: SeriesExtractionDiagnostics) => {
    const fingerprint = diagnosticsFingerprint(diagnostics);
    const now = dependencies.now?.() ?? Date.now();
    failure = currentFailureState(failure, fingerprint, now);

    updateSnapshot({
      status: 'starting',
      providerSeriesId: diagnostics.providerSeriesId,
      seriesTitle: diagnostics.seriesTitle,
      seasonTitle: diagnostics.seasonTitle,
      observedEpisodeCount: diagnostics.observedEpisodeCount,
      message: extractionIssueMessage(diagnostics.issue),
    });

    const age = now - failure.firstSeenAt;
    if (age < FAILURE_WARNING_DELAY_MS) {
      scheduleFailureRescan(age);
      return;
    }
    if (failure.warned) return;

    failure.warned = true;
    updateSnapshot({ status: 'error' });
    console.warn(extensionLogMessage(extractionIssueMessage(diagnostics.issue)), diagnostics);
  };

  const scheduleFailureRescan = (failureAge: number) => {
    if (scanTimer !== null) clearTimeout(scanTimer);
    scanTimer = ctx.setTimeout(() => {
      scanTimer = null;
      void runScan();
    }, Math.max(SCAN_DELAY_MS, FAILURE_WARNING_DELAY_MS - failureAge));
  };

  const applyDeliveryResult = (
    result: MessageResult<CatalogSubmissionResult>,
    correlationId: string,
  ) => {
    logVerbose('Cantaro: Crunchyroll catalog delivery result', { correlationId, result });
    if (!result.ok) {
      updateSnapshot({ status: 'error', message: result.error.message });
      console.warn(extensionLogMessage('Crunchyroll catalog delivery failed'), {
        correlationId,
        error: sanitizeDiagnosticDetails(result.error),
      });
      return;
    }

    const status = result.value.status;
    updateSnapshot({
      status: catalogControllerStatus(status),
      submissionStatus: status,
      message: catalogStatusMessage(result.value),
    });
    console.info(extensionLogMessage('Crunchyroll catalog snapshot handled'), {
      correlationId,
      status,
      providerSeriesId: snapshot.providerSeriesId,
      seasonTitle: snapshot.seasonTitle,
      episodeCount: snapshot.observedEpisodeCount,
    });
  };

  const observer = new MutationObserver(requestScan);
  const attachObserver = () => {
    if (!collectionAllowed || ctx.isInvalid) return;
    observer.observe(document.documentElement, { childList: true, subtree: true });
  };
  notifyContextChanged = startMediaControllerRuntime(ctx, dependencies, () => snapshot, (enabled) => {
    verboseLogging = enabled;
  }, () => {
    if (!collectionAllowed) return;
    logVerbose('Cantaro: Crunchyroll series controller started', {
      pageUrl: stripUrlQueryAndFragment(location.href),
    });
    requestScan();
  }, () => collectionAllowed && isCurrentSeriesPage(), () => collectionAllowed);

  const clearCollectionState = () => {
    if (scanTimer !== null) {
      clearTimeout(scanTimer);
      scanTimer = null;
    }
    scanRequested = false;
    failure = null;
    lastSubmittedFingerprint = '';
    lastLoggedFingerprint = '';
    updateSnapshot({
      status: 'starting',
      providerSeriesId: undefined,
      seriesTitle: undefined,
      seasonTitle: undefined,
      observedEpisodeCount: 0,
      submissionStatus: undefined,
      message: undefined,
    });
  };

  const handleCollectionState = createContentCollectionStateHandler(
    () => collectionAllowed,
    allowed => { collectionAllowed = allowed; },
    () => {
      observer.disconnect();
      clearCollectionState();
    },
    attachObserver,
    requestScan,
  );
  collectionGate = createContentCollectionGate(dependencies, handleCollectionState);
  ctx.onInvalidated(() => collectionGate.dispose());

  ctx.addEventListener(window, 'wxt:locationchange', requestScan);
  ctx.onInvalidated(() => {
    observer.disconnect();
    if (scanTimer !== null) clearTimeout(scanTimer);
    clearCollectionState();
  });

  return {
    getSnapshot: () => snapshot,
    requestScan,
    dispose: () => ctx.abort(),
  };
}

function isCurrentSeriesPage(): boolean {
  return extractSeriesId(location.pathname) !== undefined;
}

function createInitialSnapshot(collectionAllowed: boolean): MediaSeriesTabContext {
  return {
    feature: 'media',
    provider: 'crunchyroll',
    pageKind: 'series',
    pageUrl: collectionAllowed ? stripUrlQueryAndFragment(location.href) : '',
    status: 'starting',
    observedEpisodeCount: 0,
  };
}

function diagnosticsFingerprint(diagnostics: SeriesExtractionDiagnostics): string {
  return JSON.stringify([
    diagnostics.issue,
    diagnostics.pageUrl,
    diagnostics.providerSeriesId,
    diagnostics.seasonTitle,
    diagnostics.episodeCardCount,
    diagnostics.watchLinkCount,
    diagnostics.labelledWatchLinkCount,
  ]);
}

function currentFailureState(
  current: FailureState | null,
  fingerprint: string,
  now: number,
): FailureState {
  if (current?.fingerprint === fingerprint) return current;
  return { fingerprint, firstSeenAt: now, warned: false };
}

function isDeliveredCatalog(
  result: MessageResult<CatalogSubmissionResult>,
): boolean {
  return result.ok && result.value.status !== 'rejected';
}

function catalogControllerStatus(
  status: CatalogSubmissionResult['status'],
): MediaSeriesTabContext['status'] {
  if (status === 'queued') return 'queued';
  if (status === 'rejected') return 'error';
  return 'submitted';
}

function catalogStatusMessage(result: CatalogSubmissionResult): string | undefined {
  if (result.status === 'pending_match') {
    return 'Episode links were collected and are waiting for a catalog match.';
  }
  if (result.status === 'queued') {
    return 'Episode links were collected and queued for delivery.';
  }
  return result.reason;
}

function extractionIssueMessage(issue: SeriesExtractionIssue | undefined): string {
  switch (issue) {
    case 'invalid_series_url':
      return 'The current URL is not a supported Crunchyroll series page.';
    case 'missing_series_title':
      return 'Could not find the Crunchyroll series title after the page rendered.';
    case 'missing_season_title':
      return 'Could not determine the selected Crunchyroll season after the page rendered.';
    case 'no_rendered_episodes':
      return 'The series page rendered, but no safe episode URLs were found.';
    default:
      return 'Could not collect episode URLs from the Crunchyroll series page.';
  }
}
