import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'wxt';

function readEnvValue(name: string): string | undefined {
  const exactMatch = process.env[name];
  if (exactMatch) {
    return exactMatch;
  }

  const caseInsensitiveKey = Object.keys(process.env).find(
    (key) => key.toLowerCase() === name.toLowerCase(),
  );

  return caseInsensitiveKey ? process.env[caseInsensitiveKey] : undefined;
}

const defaultApiBaseUrl = readEnvValue('services__api__https__0')
  ?? readEnvValue('services__api__http__0')
  ?? readEnvValue('CANTARO_API_BASE_URL')
  ?? readEnvValue('WXT_API_BASE_URL')
  ?? 'http://localhost:5000';

// https://wxt.dev/api/config.html
export default defineConfig({
  manifest: {
    name: 'Cantaro',
    description: 'Accelerate playlist sync between your music services',
    version: '0.1.0',
    permissions: [
      'storage',
      'tabs',
      'identity',
    ],
    host_permissions: [
      'http://*/*',
      'https://*/*',
      'https://open.spotify.com/*',
      'https://music.youtube.com/*',
      'https://www.youtube.com/*',
      'https://www.crunchyroll.com/*',
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
  modules: ['@wxt-dev/module-react'],
  vite: () => ({
    plugins: [tailwindcss()],
    define: {
      __CANTARO_DEFAULT_API_BASE_URL__: JSON.stringify(defaultApiBaseUrl),
    },
    build: {
      sourcemap: false,
    },
  }),
});
