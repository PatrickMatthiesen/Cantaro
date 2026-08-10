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

const defaultBaseUrl = readEnvValue('services__web__https__0')
  ?? readEnvValue('WEB_HTTP')
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

const defaultApiHostPermission = toOriginMatchPattern(defaultBaseUrl);

console.log('defaultApiBaseUrl:', defaultBaseUrl);
console.log('defaultWebBaseUrl:', defaultBaseUrl);
console.log('defaultApiHostPermission:', defaultApiHostPermission);

console.log('process.env.services__web__https__0:', process.env.services__web__https__0);
console.log('process.env.CANTARO_WEB_BASE_URL:', process.env.CANTARO_WEB_BASE_URL);
console.log('process.env.WXT_API_BASE_URL:', process.env.WXT_API_BASE_URL);
console.log('process.env.CANTARO_API_BASE_URL:', process.env.CANTARO_API_BASE_URL);
console.log('process.env.services__api__https__0:', process.env.services__api__https__0);
console.log('process.env.services__api__http__0:', process.env.services__api__http__0);

// https://wxt.dev/api/config.html
export default defineConfig({
  manifest: {
    name: 'Cantaro',
    description: 'Sync music and collect rendered episode URLs from supported streaming pages',
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
  dev: {
    server: {
      port: 5173,
    },
  },
  modules: ['@wxt-dev/module-react'],
  vite: () => ({
    plugins: [tailwindcss()],
    define: {
      __CANTARO_BASE_URL__: JSON.stringify(defaultBaseUrl),
    },
    build: {
      sourcemap: false,
    },
  }),
});
