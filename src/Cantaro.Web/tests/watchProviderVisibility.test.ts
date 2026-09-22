import { describe, expect, it } from 'bun:test';
import type { ProfilePreferences } from '@cantaro/client-shared/auth';
import { STREAMING_SERVICE_IDS } from '@cantaro/client-shared/media';
import {
  isWatchProviderVisible,
  setWatchProviderVisible,
  watchProviderOptions,
} from '../src/settings/watchProviderVisibility';

const preferences: ProfilePreferences = {
  theme: 'system',
  preferredMediaReleaseTrack: 'sub:en',
  notifyOnSyncSuccess: true,
  notifyOnSyncFailure: true,
  notifyOnMediaReview: true,
  keepPlaylistOrder: true,
  keepPlaylistMetadata: true,
  hideUnavailableTracks: false,
  scheduledSync: false,
  blurEmailAddress: false,
};

describe('watch-provider visibility preferences', () => {
  it('shows every provider until the account has explicitly disabled it', () => {
    for (const providerId of STREAMING_SERVICE_IDS) {
      expect(isWatchProviderVisible(preferences, providerId)).toBeTrue();
    }
  });

  it('uses the shared streaming-provider registry for the settings catalog', () => {
    expect(watchProviderOptions.map(provider => provider.id)).toEqual(STREAMING_SERVICE_IDS);
  });

  it('only changes the selected provider while preserving other preferences', () => {
    const hidden = setWatchProviderVisible(preferences, 'crunchyroll', false);

    expect(hidden).toEqual({ ...preferences, disabledWatchProviders: ['crunchyroll'] });
    expect(isWatchProviderVisible(hidden, 'crunchyroll')).toBeFalse();

    const visible = setWatchProviderVisible(hidden, 'crunchyroll', true);
    expect(visible).toEqual({ ...preferences, disabledWatchProviders: [] });
  });

  it('retains disabled provider ids the current registry does not recognize', () => {
    const next = setWatchProviderVisible({ ...preferences, disabledWatchProviders: ['future-provider'] }, 'crunchyroll', false);

    expect(next.disabledWatchProviders).toEqual(['future-provider', 'crunchyroll']);
  });
});
