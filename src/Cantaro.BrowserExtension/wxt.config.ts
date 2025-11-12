import { defineConfig } from 'wxt';

// https://wxt.dev/api/config.html
export default defineConfig({
  manifest: {
    name: 'Cantaro',
    description: 'Accelerate playlist sync between your music services',
    version: '0.1.0',
    permissions: [
      'storage',
      'tabs',
    ],
    host_permissions: [
      'https://open.spotify.com/*',
      'https://music.youtube.com/*',
      'https://www.youtube.com/*',
    ],
  },
  webExt: {
    // disabled: false,
  },
  dev: {
    server: {
      port: 5174,
    },
  },
});
