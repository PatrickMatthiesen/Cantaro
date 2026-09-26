import { afterEach, describe, expect, it, vi } from 'vitest';
import { SETTINGS_STORAGE_KEY } from '../../platform/settings/settingsRepository';
import { cantaroApiClient } from '../../platform/api/cantaroApiClient';
import { openCantaroPage, removeSongFromPlaylist } from './musicService';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('removes the selected playlist occurrence by entry ID', async () => {
  const request = vi.spyOn(cantaroApiClient, 'request').mockResolvedValue(undefined);

  await removeSongFromPlaylist('track', 'playlist', 'video', 'second-entry');

  expect(request).toHaveBeenCalledWith('/api/music/library/playlists/playlist/songs/track?youtubeVideoId=video&entryId=second-entry', { method: 'DELETE' });
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
