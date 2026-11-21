import {
  normalizeExtensionSettings,
  type ExtensionSettings,
} from './extensionSettings';
import { browserStorage, type ExtensionStorage } from '../storage/browserStorage';

export const SETTINGS_STORAGE_KEY = 'cantaro.settings.v1';

export interface SettingsRepository {
  read(): Promise<ExtensionSettings>;
  save(settings: ExtensionSettings): Promise<ExtensionSettings>;
}

function isSettingsRecord(value: unknown): value is Partial<ExtensionSettings> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function createSettingsRepository(storage: Pick<ExtensionStorage, 'get' | 'set'>): SettingsRepository {
  return {
    async read() {
      const stored = await storage.get(SETTINGS_STORAGE_KEY);
      const versioned = stored[SETTINGS_STORAGE_KEY];
      return normalizeExtensionSettings(isSettingsRecord(versioned) ? versioned : {});
    },

    async save(settings) {
      const normalized = normalizeExtensionSettings(settings);
      await storage.set({ [SETTINGS_STORAGE_KEY]: normalized });
      return normalized;
    },
  };
}

export const browserSettingsRepository = createSettingsRepository(browserStorage('local'));
