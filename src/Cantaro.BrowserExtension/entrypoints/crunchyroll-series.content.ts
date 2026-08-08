import {
  inspectCrunchyrollSeriesPage,
  seriesObservationFingerprint,
  type CrunchyrollSeriesExtractionDiagnostics,
  type CrunchyrollSeriesExtractionIssue,
} from '../lib/crunchyrollSeriesCatalog';
import { isEpisodeTrackingTimedOut } from '../lib/episodeTrackingTimeout';
import { readExtensionConfig } from '../lib/extensionRuntimeConfig';
import type {
  MediaObservation,
  MediaObservationDeliveryResult,
  MediaObservationMessage,
} from '../lib/mediaObservation';

const SCAN_DELAY_MS = 750;
const FAILURE_WARNING_DELAY_MS = 5_000;

export default defineContentScript({
  matches: [
    'https://www.crunchyroll.com/series/*',
    'https://www.crunchyroll.com/*/series/*',
  ],

  main() {
    void initializeSeriesUrlCollector().catch((error: unknown) => {
      console.error('Cantaro: Crunchyroll series URL collector failed to start', error);
    });
  },
});

async function initializeSeriesUrlCollector(): Promise<void> {
  let verboseLogging = (await readExtensionConfig()).verboseLogging;
  const verboseLog = (message: string, details?: unknown) => {
    if (!verboseLogging) return;
    if (details === undefined) console.debug(message);
    else console.debug(message, details);
  };

  verboseLog('Cantaro: Crunchyroll series URL collector loaded', { pageUrl: location.href });
  startSeriesUrlCollector(verboseLog);

  browser.storage.onChanged.addListener((changes, areaName) => {
    if (areaName !== 'local' || !changes.verboseLogging) return;
    verboseLogging = changes.verboseLogging.newValue === true;
    console.info(`Cantaro: verbose logging ${verboseLogging ? 'enabled' : 'disabled'}`);
  });
}

type VerboseLog = (message: string, details?: unknown) => void;
type ScheduleScan = (delayMs?: number) => void;

interface ExtractionFailureState {
  pendingFingerprint: string;
  pendingSince: number;
  warnedFingerprint: string;
}

function startSeriesUrlCollector(verboseLog: VerboseLog): void {
  let lastFingerprint = '';
  let scanTimer: ReturnType<typeof setTimeout> | null = null;
  const failureState: ExtractionFailureState = {
    pendingFingerprint: '',
    pendingSince: 0,
    warnedFingerprint: '',
  };

  const scan = async () => {
    scanTimer = null;
    try {
      if (await isEpisodeTrackingTimedOut()) {
        verboseLog('Cantaro: Crunchyroll series URL scan skipped because episode tracking is paused');
        return;
      }

      const extraction = inspectCrunchyrollSeriesPage(document, location);
      verboseLog('Cantaro: Crunchyroll series page scan completed', extraction.diagnostics);
      if (!extraction.observation) {
        handleExtractionFailure(extraction.diagnostics, failureState, scheduleScan);
        return;
      }

      resetExtractionFailure(failureState);
      lastFingerprint = await deliverEpisodeBatch(
        extraction.observation,
        extraction.diagnostics,
        lastFingerprint,
        verboseLog,
      );
    } catch (error) {
      lastFingerprint = '';
      console.error('Cantaro: Crunchyroll series URL collection failed', {
        error,
        pageUrl: location.href,
      });
    }
  };

  const scheduleScan = (delayMs = SCAN_DELAY_MS) => {
    if (scanTimer !== null) clearTimeout(scanTimer);
    scanTimer = setTimeout(() => void scan(), delayMs);
  };

  scheduleScan();
  const observer = new MutationObserver(() => scheduleScan());
  observer.observe(document.documentElement, { childList: true, subtree: true });
}

function handleExtractionFailure(
  diagnostics: CrunchyrollSeriesExtractionDiagnostics,
  state: ExtractionFailureState,
  scheduleScan: ScheduleScan,
): void {
  const failureFingerprint = diagnosticsFingerprint(diagnostics);
  const now = Date.now();
  if (failureFingerprint !== state.pendingFingerprint) {
    state.pendingFingerprint = failureFingerprint;
    state.pendingSince = now;
  }

  const failureAge = now - state.pendingSince;
  if (failureAge < FAILURE_WARNING_DELAY_MS) {
    scheduleScan(Math.max(SCAN_DELAY_MS, FAILURE_WARNING_DELAY_MS - failureAge));
    return;
  }
  if (state.warnedFingerprint === failureFingerprint) return;

  state.warnedFingerprint = failureFingerprint;
  warnAboutExtractionFailure(diagnostics);
}

function resetExtractionFailure(state: ExtractionFailureState): void {
  state.pendingFingerprint = '';
  state.pendingSince = 0;
  state.warnedFingerprint = '';
}

async function deliverEpisodeBatch(
  observation: MediaObservation,
  diagnostics: CrunchyrollSeriesExtractionDiagnostics,
  lastFingerprint: string,
  verboseLog: VerboseLog,
): Promise<string> {
  const fingerprint = seriesObservationFingerprint(observation);
  if (fingerprint === lastFingerprint) {
    verboseLog('Cantaro: unchanged Crunchyroll episode batch skipped', { fingerprint });
    return lastFingerprint;
  }

  verboseLog('Cantaro: extracted rendered Crunchyroll episode URLs', {
    fingerprint,
    diagnostics,
    episodes: observation.observedEpisodes,
  });
  const delivery = await browser.runtime.sendMessage({
    type: 'MEDIA_OBSERVATION',
    payload: observation,
  } satisfies MediaObservationMessage) as MediaObservationDeliveryResult;
  verboseLog('Cantaro: Crunchyroll episode batch delivery completed', delivery);
  warnAboutIncompleteDelivery(delivery, diagnostics);
  return fingerprint;
}

function diagnosticsFingerprint(diagnostics: CrunchyrollSeriesExtractionDiagnostics): string {
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

function warnAboutExtractionFailure(diagnostics: CrunchyrollSeriesExtractionDiagnostics): void {
  console.warn(`Cantaro: ${extractionIssueMessage(diagnostics.issue)}`, diagnostics);
}

function extractionIssueMessage(issue: CrunchyrollSeriesExtractionIssue | undefined): string {
  switch (issue) {
    case 'invalid_series_url':
      return 'the current URL is not a supported Crunchyroll series page';
    case 'missing_series_title':
      return 'could not find the Crunchyroll series title after waiting for the page to render';
    case 'missing_season_title':
      return 'could not determine the selected Crunchyroll season after waiting for the page to render';
    case 'no_rendered_episodes':
      return 'found the Crunchyroll series page but could not find any safe rendered episode URLs';
    default:
      return 'could not collect episode URLs from the Crunchyroll series page';
  }
}

function warnAboutIncompleteDelivery(
  delivery: MediaObservationDeliveryResult,
  diagnostics: CrunchyrollSeriesExtractionDiagnostics,
): void {
  if (delivery.delivery === 'queued_no_session') {
    console.warn('Cantaro: episode URLs were extracted but only queued because the extension is not signed in', {
      delivery,
      diagnostics,
    });
    return;
  }
  if (delivery.delivery === 'queued_send_failed') {
    console.warn('Cantaro: episode URLs were extracted but the Cantaro API rejected or could not receive them; the batch was queued for retry', {
      delivery,
      diagnostics,
    });
    return;
  }
  if (delivery.requiresResolution) {
    console.warn('Cantaro: episode URLs reached the API, but the observed series must be matched or resolved before the links can become catalog destinations', {
      delivery,
      diagnostics,
    });
  }
}
