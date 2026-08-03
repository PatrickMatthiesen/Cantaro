import {
  buildCrunchyrollSeriesObservation,
  seriesObservationFingerprint,
} from '../lib/crunchyrollSeriesCatalog';
import { isEpisodeTrackingTimedOut } from '../lib/episodeTrackingTimeout';
import type { MediaObservationMessage } from '../lib/mediaObservation';

const SCAN_DELAY_MS = 750;

export default defineContentScript({
  matches: [
    'https://www.crunchyroll.com/series/*',
    'https://www.crunchyroll.com/*/series/*',
  ],

  main() {
    console.log('Cantaro: Crunchyroll series URL collector loaded');
    startSeriesUrlCollector();
  },
});

function startSeriesUrlCollector(): void {
  let lastFingerprint = '';
  let scanTimer: ReturnType<typeof setTimeout> | null = null;

  const scan = async () => {
    scanTimer = null;
    if (await isEpisodeTrackingTimedOut()) return;

    const observation = buildCrunchyrollSeriesObservation(document, location);
    if (!observation) return;

    const fingerprint = seriesObservationFingerprint(observation);
    if (fingerprint === lastFingerprint) return;
    lastFingerprint = fingerprint;

    console.log('Cantaro: observed rendered Crunchyroll episode URLs', {
      seriesId: observation.providerSeriesId,
      seasonTitle: observation.seasonTitle,
      episodeCount: observation.observedEpisodes?.length ?? 0,
    });

    await browser.runtime.sendMessage({
      type: 'MEDIA_OBSERVATION',
      payload: observation,
    } satisfies MediaObservationMessage).catch((error: unknown) => {
      lastFingerprint = '';
      console.warn('Cantaro: failed to send rendered episode URLs', error);
    });
  };

  const scheduleScan = () => {
    if (scanTimer !== null) clearTimeout(scanTimer);
    scanTimer = setTimeout(() => void scan(), SCAN_DELAY_MS);
  };

  scheduleScan();
  const observer = new MutationObserver(scheduleScan);
  observer.observe(document.documentElement, { childList: true, subtree: true });
}
