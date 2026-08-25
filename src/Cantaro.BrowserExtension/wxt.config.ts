import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'wxt';
import packageMetadata from './package.json';

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
const repositoryRoot = fileURLToPath(new URL('../..', import.meta.url));

// https://wxt.dev/api/config.html
export default defineConfig({
  manifestVersion: 3,
  manifest: ({ browser }) => ({
    name: 'Cantaro',
    description: 'Sync music and collect rendered episode URLs from supported streaming pages',
    version: packageMetadata.version,
    icons: {
      16: '/icon/16.png',
      32: '/icon/32.png',
      48: '/icon/48.png',
      96: '/icon/96.png',
      128: '/icon/128.png',
    },
    permissions: [
      'storage',
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
    optional_host_permissions: ['https://*/*'],
    ...(browser === 'firefox' ? {
      browser_specific_settings: {
        gecko: {
          id: 'cantaro@bmstack.net',
          strict_min_version: '140.0',
          data_collection_permissions: {
            required: [
              'authenticationInfo',
              'browsingActivity',
              'websiteContent',
              'websiteActivity',
            ],
          },
        },
      },
    } : {}),
  }),
  dev: {
    server: {
      port: 5174,
    },
  },
  zip: {
    sourcesRoot: repositoryRoot,
    includeSources: [
      'package.json',
      'bun.lock',
      'src/Cantaro.BrowserExtension/app/**',
      'src/Cantaro.BrowserExtension/entrypoints/**',
      'src/Cantaro.BrowserExtension/features/**',
      'src/Cantaro.BrowserExtension/platform/**',
      'src/Cantaro.BrowserExtension/public/**',
      'src/Cantaro.BrowserExtension/package.json',
      'src/Cantaro.BrowserExtension/tsconfig.json',
      'src/Cantaro.BrowserExtension/wxt.config.ts',
      'src/Cantaro.ClientShared/src/**',
      'src/Cantaro.ClientShared/package.json',
      'src/Cantaro.ClientShared/tsconfig.json',
      'src/Cantaro.Web/package.json',
    ],
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
