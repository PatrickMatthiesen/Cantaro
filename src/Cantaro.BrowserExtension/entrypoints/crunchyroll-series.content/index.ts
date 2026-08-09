import { startCrunchyrollSeriesContent } from '../../features/media/content/providers/crunchyroll/series/startSeriesContent';

export default defineContentScript({
  matches: [
    'https://www.crunchyroll.com/series/*',
    'https://www.crunchyroll.com/*/series/*',
  ],
  main(ctx) {
    startCrunchyrollSeriesContent(ctx);
  },
});
