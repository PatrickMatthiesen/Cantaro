export default defineContentScript({
  matches: ['https://open.spotify.com/*'],

  main() {
    console.log('Cantaro content script loaded on Spotify');
    // Playlist event detection for Spotify is not yet implemented.
    // This entry point is a placeholder; the background worker handles
    // any future PLAYLIST_EVENT messages sent from here.
  },
});
