import { startYouTubeLyrics } from '../../features/music/youtube/startYouTubeLyrics';

export default defineContentScript({
  matches: ['https://www.youtube.com/*', 'https://music.youtube.com/*'],
  main(ctx) {
    startYouTubeLyrics(ctx);
  },
});
