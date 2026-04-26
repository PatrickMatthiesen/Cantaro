import { buildCrunchyrollObservation } from '../lib/crunchyrollAdapter';
import type { MediaObservationMessage } from '../lib/mediaObservation';

export default defineContentScript({
  matches: ['https://www.crunchyroll.com/watch/*'],

  main() {
    console.log('Cantaro: Crunchyroll media observer loaded');

    // Observe the initial page load
    observeCurrentPage();

    // Re-observe after Crunchyroll's SPA navigation updates the URL/DOM
    observeNavigation();
  },
});

/**
 * Extract an observation from the current page state and send it to the
 * background worker.  No-ops when the page cannot produce a valid observation
 * (e.g. the URL is not a watch page).
 */
function observeCurrentPage(): void {
  const observation = buildCrunchyrollObservation(location.href, document);
  if (!observation) return;

  const message: MediaObservationMessage = {
    type: 'MEDIA_OBSERVATION',
    payload: observation,
  };

  browser.runtime.sendMessage(message).catch((err: unknown) => {
    console.warn('Cantaro: failed to send media observation', err);
  });
}

/**
 * Watch for Crunchyroll's SPA navigation so that moving between episodes
 * within the same tab triggers fresh observations.
 *
 * Crunchyroll updates `document.title` on navigation; we detect that change
 * via a MutationObserver on <title> as a lightweight signal.  A debounce
 * prevents duplicate sends when the SPA updates the title in multiple steps.
 */
function observeNavigation(): void {
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  const titleEl = document.querySelector('title');
  if (!titleEl) return;

  const observer = new MutationObserver(() => {
    if (debounceTimer !== null) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      // Only emit when we are still on a watch page
      if (location.pathname.startsWith('/watch/')) {
        observeCurrentPage();
      }
    }, 500);
  });

  observer.observe(titleEl, { childList: true });
}
