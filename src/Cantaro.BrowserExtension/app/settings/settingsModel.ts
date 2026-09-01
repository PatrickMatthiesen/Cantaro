import {
  DEFAULT_BASE_URL,
  normalizeBaseUrl,
  type ExtensionSettings,
} from '../../platform/settings/extensionSettings';

export type SettingsDraft = ExtensionSettings;

export function draftFromSettings(settings: ExtensionSettings): SettingsDraft {
  return { ...settings };
}

export function createSettingsUpdate(savedSettings: ExtensionSettings, draft: SettingsDraft) {
  const baseUrl = normalizeBaseUrl(draft.baseUrl) || DEFAULT_BASE_URL;
  const settings = {
    baseUrl: normalizeBaseUrl(draft.baseUrl) || DEFAULT_BASE_URL,
    verboseLogging: draft.verboseLogging,
  } satisfies ExtensionSettings;
  return { settings, baseUrlChanged: baseUrl !== savedSettings.baseUrl };
}

export function hasSettingsChanges(savedSettings: ExtensionSettings, draft: SettingsDraft) {
  const normalized = createSettingsUpdate(savedSettings, draft).settings;
  return normalized.baseUrl !== savedSettings.baseUrl
    || normalized.verboseLogging !== savedSettings.verboseLogging;
}
