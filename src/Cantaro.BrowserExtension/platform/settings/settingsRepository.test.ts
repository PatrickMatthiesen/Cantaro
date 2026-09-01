import { describe, expect, it } from 'vitest';
import { createSettingsRepository, SETTINGS_STORAGE_KEY } from './settingsRepository';

function memoryStorage(initial: Record<string, unknown> = {}) {
  const values = { ...initial };
  return {
    values,
    async get(keys: string | string[]) {
      const requested = Array.isArray(keys) ? keys : [keys];
      return Object.fromEntries(requested.map((key) => [key, values[key]]));
    },
    async set(items: Record<string, unknown>) {
      Object.assign(values, items);
    },
  };
}

describe('settingsRepository', () => {
  it('uses defaults when the versioned record is absent', async () => {
    const storage = memoryStorage({
      baseUrl: 'https://api.example.test/',
      webBaseUrl: 'https://cantaro.example.test/',
      verboseLogging: true,
    });
    const repository = createSettingsRepository(storage);

    const settings = await repository.read();

    expect(settings.baseUrl).not.toBe('https://api.example.test');
    expect(settings.verboseLogging).toBe(false);
    expect(storage.values[SETTINGS_STORAGE_KEY]).toBeUndefined();
  });

  it('reads the versioned settings record', async () => {
    const storage = memoryStorage({
      [SETTINGS_STORAGE_KEY]: {
        baseUrl: 'https://new.example.test',
        webBaseUrl: 'https://web.example.test',
        verboseLogging: false,
      },
    });

    expect((await createSettingsRepository(storage).read()).baseUrl)
      .toBe('https://new.example.test');
  });
});
