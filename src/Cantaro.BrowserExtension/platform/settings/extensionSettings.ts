declare const __CANTARO_DEFAULT_API_BASE_URL__: string;
declare const __CANTARO_DEFAULT_WEB_BASE_URL__: string;

export interface ExtensionSettings {
  apiBaseUrl: string;
  webBaseUrl: string;
  injectLyricsOnYouTube: boolean;
  verboseLogging: boolean;
}
function readBuildDefault(name: 'api' | 'web'): string {
  if (name === 'api') {
    return typeof __CANTARO_DEFAULT_API_BASE_URL__ === 'string'
      ? __CANTARO_DEFAULT_API_BASE_URL__
      : '';
  }

  return typeof __CANTARO_DEFAULT_WEB_BASE_URL__ === 'string'
    ? __CANTARO_DEFAULT_WEB_BASE_URL__
    : '';
}

export function normalizeBaseUrl(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed.replace(/\/+$/, '') : '';
}

export const DEFAULT_API_BASE_URL = normalizeBaseUrl(readBuildDefault('api'))
  || 'https://localhost:7203';
export const DEFAULT_WEB_BASE_URL = normalizeBaseUrl(readBuildDefault('web'))
  || 'https://localhost:5173';

export const defaultExtensionSettings: ExtensionSettings = {
  apiBaseUrl: DEFAULT_API_BASE_URL,
  webBaseUrl: DEFAULT_WEB_BASE_URL,
  injectLyricsOnYouTube: false,
  verboseLogging: false,
};

export function normalizeExtensionSettings(
  settings: Partial<ExtensionSettings>,
): ExtensionSettings {
  return {
    apiBaseUrl: normalizeBaseUrl(settings.apiBaseUrl) || DEFAULT_API_BASE_URL,
    webBaseUrl: normalizeBaseUrl(settings.webBaseUrl) || DEFAULT_WEB_BASE_URL,
    injectLyricsOnYouTube: settings.injectLyricsOnYouTube === true,
    verboseLogging: settings.verboseLogging === true,
  };
}
