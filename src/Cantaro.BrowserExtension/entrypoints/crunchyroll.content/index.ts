import { startCrunchyrollSeriesContent } from '../../features/media/content/providers/crunchyroll/series/startSeriesContent';
import { startCrunchyrollWatchContent } from '../../features/media/content/providers/crunchyroll/watch/startWatchContent';

export default defineContentScript({
  matches: ['https://www.crunchyroll.com/*'],
  main(ctx) {
    // Crunchyroll changes between series and watch routes without loading a new
    // document. Both route-aware controllers must exist before that SPA
    // navigation or Chrome will never inject the newly matching script.
    startCrunchyrollSeriesContent(ctx);
    startCrunchyrollWatchContent(ctx);
  },
});
