import { startCrunchyrollWatchContent } from '../../features/media/content/providers/crunchyroll/watch/startWatchContent';

export default defineContentScript({
  matches: [
    'https://www.crunchyroll.com/watch/*',
    'https://www.crunchyroll.com/*/watch/*',
  ],
  main(ctx) {
    startCrunchyrollWatchContent(ctx);
  },
});
