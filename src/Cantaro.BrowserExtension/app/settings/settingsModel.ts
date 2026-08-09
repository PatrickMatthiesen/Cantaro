import {
  DEFAULT_API_BASE_URL,
  DEFAULT_WEB_BASE_URL,
  normalizeBaseUrl,
  type ExtensionSettings,
} from '../../platform/settings/extensionSettings';

export type SettingsDraft = ExtensionSettings;

export function draftFromSettings(settings: ExtensionSettings): SettingsDraft {
  return { ...settings };
}

export function createSettingsUpdate(savedSettings: ExtensionSettings, draft: SettingsDraft) {
  const apiBaseUrl = normalizeBaseUrl(draft.apiBaseUrl) || DEFAULT_API_BASE_URL;
  const settings = {
    apiBaseUrl,
    webBaseUrl: normalizeBaseUrl(draft.webBaseUrl) || DEFAULT_WEB_BASE_URL,
    injectLyricsOnYouTube: draft.injectLyricsOnYouTube,
    verboseLogging: draft.verboseLogging,
  } satisfies ExtensionSettings;
  return { settings, apiBaseUrlChanged: apiBaseUrl !== savedSettings.apiBaseUrl };
}

export function hasSettingsChanges(savedSettings: ExtensionSettings, draft: SettingsDraft) {
  const normalized = createSettingsUpdate(savedSettings, draft).settings;
  return normalized.apiBaseUrl !== savedSettings.apiBaseUrl
    || normalized.webBaseUrl !== savedSettings.webBaseUrl
    || normalized.injectLyricsOnYouTube !== savedSettings.injectLyricsOnYouTube
    || normalized.verboseLogging !== savedSettings.verboseLogging;
}
