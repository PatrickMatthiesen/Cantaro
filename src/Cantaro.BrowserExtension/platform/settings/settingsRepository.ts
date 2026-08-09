import {
  normalizeExtensionSettings,
  type ExtensionSettings,
} from './extensionSettings';

export const SETTINGS_STORAGE_KEY = 'cantaro.settings.v1';

interface StorageArea {
  get(key: string): Promise<Record<string, unknown>>;
  set(items: Record<string, unknown>): Promise<void>;
}

export interface SettingsRepository {
  read(): Promise<ExtensionSettings>;
  save(settings: ExtensionSettings): Promise<ExtensionSettings>;
}

function isSettingsRecord(value: unknown): value is Partial<ExtensionSettings> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function createSettingsRepository(storage: StorageArea): SettingsRepository {
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

export const browserSettingsRepository = createSettingsRepository({
  get: (key) => browser.storage.local.get(key),
  set: (items) => browser.storage.local.set(items),
});

export function watchExtensionSettings(
  listener: (settings: ExtensionSettings) => void,
): () => void {
  const onChanged = (
    changes: Record<string, Browser.storage.StorageChange>,
    areaName: string,
  ) => {
    const next = changes[SETTINGS_STORAGE_KEY]?.newValue;
    if (areaName === 'local' && isSettingsRecord(next)) {
      listener(normalizeExtensionSettings(next));
    }
  };
  browser.storage.onChanged.addListener(onChanged);
  return () => browser.storage.onChanged.removeListener(onChanged);
}
