import { startSpotifyContent } from '../../features/music/content/providers/spotify/startSpotifyContent';

export default defineContentScript({
  matches: ['https://open.spotify.com/*'],
  main(ctx) {
    startSpotifyContent(ctx);
  },
});
