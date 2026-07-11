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
  ?? 'https://localhost:7203';
const defaultWebBaseUrl = readEnvValue('services__web__http__0')
  ?? readEnvValue('CANTARO_WEB_BASE_URL')
  ?? 'https://localhost:5173';

function toOriginMatchPattern(value: string): string | null {
  try {
    const url = new URL(value);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
      return null;
    }

    return `${url.origin}/*`;
  } catch {
    return null;
  }
}

const defaultApiHostPermission = toOriginMatchPattern(defaultApiBaseUrl);

// https://wxt.dev/api/config.html
export default defineConfig({
  manifest: {
    name: 'Cantaro',
    description: 'Accelerate playlist sync between your music services',
    version: '0.1.0',
    icons: {
      16: '/icon/16.png',
      32: '/icon/32.png',
      48: '/icon/48.png',
      96: '/icon/96.png',
      128: '/icon/128.png',
    },
    permissions: [
      'storage',
      'tabs',
      'identity',
    ],
    host_permissions: [
      ...(defaultApiHostPermission ? [defaultApiHostPermission] : []),
      'https://open.spotify.com/*',
      'https://music.youtube.com/*',
      'https://www.youtube.com/*',
      'https://youtube.com/*',
      'https://www.crunchyroll.com/*',
    ],
    optional_host_permissions: [
      'http://*/*',
      'https://*/*',
    ],
  },
  webExt: {
    // Avoid automation signals from WXT's Chromium dev runner on sites with bot checks.
    chromiumArgs: [
      '--disable-blink-features=AutomationControlled',
    ],
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
      __CANTARO_DEFAULT_WEB_BASE_URL__: JSON.stringify(defaultWebBaseUrl.replace(/\/+$/, '')),
    },
    build: {
      sourcemap: false,
    },
  }),
});
