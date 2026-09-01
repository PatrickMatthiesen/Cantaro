declare const __CANTARO_BASE_URL__: string;

export interface ExtensionSettings {
  baseUrl: string;
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

export function defaultVerboseLoggingForMode(mode: string): boolean {
  return mode === 'development';
}

const DEFAULT_VERBOSE_LOGGING = defaultVerboseLoggingForMode(import.meta.env.MODE);

export const defaultExtensionSettings: ExtensionSettings = {
  baseUrl: DEFAULT_BASE_URL,
  verboseLogging: DEFAULT_VERBOSE_LOGGING,
};

export function normalizeExtensionSettings(
  settings: Partial<ExtensionSettings>,
): ExtensionSettings {
  return {
    baseUrl: normalizeBaseUrl(settings.baseUrl) || DEFAULT_BASE_URL,
    verboseLogging: typeof settings.verboseLogging === 'boolean'
      ? settings.verboseLogging
      : DEFAULT_VERBOSE_LOGGING,
  };
}
