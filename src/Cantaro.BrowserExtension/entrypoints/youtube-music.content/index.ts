import { startYouTubeMusicContent } from '../../features/music/content/providers/youtube/startYouTubeContent';

export default defineContentScript({
  matches: [
    'https://www.youtube.com/*',
    'https://youtube.com/*',
    'https://music.youtube.com/*',
  ],
  main(ctx) {
    startYouTubeMusicContent(ctx);
  },
});
