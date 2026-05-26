import { defineWebExtConfig } from 'wxt';

export default defineWebExtConfig({
  disabled: false,
  startUrls: [
    'https://open.spotify.com/',
    'https://music.youtube.com/',
    'https://www.youtube.com/',
    'https://www.crunchyroll.com/',
  ],
});
