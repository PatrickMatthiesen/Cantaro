import { describe, expect, it } from 'vitest';
import type { CrunchyrollWatchMetadata } from './watchAdapter';
import { isFreshNavigationMetadata, watchMetadataFingerprint } from './navigationMetadata';

const episodeSeven: CrunchyrollWatchMetadata = {
  provider: 'crunchyroll',
  providerEpisodeId: 'EP7',
  observedUrl: 'https://www.crunchyroll.com/watch/EP7/episode-7',
  seriesTitle: 'Example Show',
  episodeTitle: 'Episode 7 - Old DOM',
  episodeNumber: 7,
  seasonTitle: 'Season 1',
  seasonNumber: 1,
};

describe('Crunchyroll navigation metadata', () => {
  it('rejects the prior DOM metadata after the URL changes episode identity', () => {
    const previous = watchMetadataFingerprint(episodeSeven);
    expect(isFreshNavigationMetadata(previous, {
      ...episodeSeven,
      providerEpisodeId: 'EP8',
      observedUrl: 'https://www.crunchyroll.com/watch/EP8/episode-8',
    })).toBe(false);
  });

  it('accepts metadata after the episode DOM changes', () => {
    const previous = watchMetadataFingerprint(episodeSeven);
    expect(isFreshNavigationMetadata(previous, {
      ...episodeSeven,
      providerEpisodeId: 'EP8',
      observedUrl: 'https://www.crunchyroll.com/watch/EP8/episode-8',
      episodeTitle: 'Episode 8 - New DOM',
      episodeNumber: 8,
    })).toBe(true);
  });
});
