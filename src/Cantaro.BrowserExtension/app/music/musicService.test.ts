import { afterEach, describe, expect, it, vi } from 'vitest';
import { SETTINGS_STORAGE_KEY } from '../../platform/settings/settingsRepository';
import { openCantaroPage } from './musicService';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('openCantaroPage', () => {
  it('opens paths against the configured unified base URL', async () => {
    const create = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('browser', {
      storage: {
        local: {
          get: vi.fn().mockResolvedValue({
            [SETTINGS_STORAGE_KEY]: {
              baseUrl: 'https://cantaro.example.test/',
            },
          }),
          set: vi.fn().mockResolvedValue(undefined),
        },
      },
      tabs: { create },
    });

    await openCantaroPage('/music/songs/42');

    expect(create).toHaveBeenCalledWith({
      url: 'https://cantaro.example.test/music/songs/42',
    });
  });
});
