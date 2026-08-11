declare const __CANTARO_BASE_URL__: string;

export interface ExtensionSettings {
  baseUrl: string;
  injectLyricsOnYouTube: boolean;
  verboseLogging: boolean;
}

export function normalizeBaseUrl(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed.replace(/\/+$/, '') : '';
}

function readBuildBaseUrl(): string {
  return typeof __CANTARO_BASE_URL__ === 'string' ? __CANTARO_BASE_URL__ : '';
}

export const DEFAULT_BASE_URL = normalizeBaseUrl(readBuildBaseUrl())
  || 'https://localhost:5173';

export const defaultExtensionSettings: ExtensionSettings = {
  baseUrl: DEFAULT_BASE_URL,
  injectLyricsOnYouTube: false,
  verboseLogging: false,
};

export function normalizeExtensionSettings(
  settings: Partial<ExtensionSettings>,
): ExtensionSettings {
  return {
    baseUrl: normalizeBaseUrl(settings.baseUrl) || DEFAULT_BASE_URL,
    injectLyricsOnYouTube: settings.injectLyricsOnYouTube === true,
    verboseLogging: settings.verboseLogging === true,
  };
}
