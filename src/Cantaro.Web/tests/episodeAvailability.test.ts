import { describe, expect, test } from 'bun:test';
import {
  formatEpisodeAvailability,
  formatReleaseAvailability,
} from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/episodeAvailability';

describe('episode availability presentation', () => {
  test('always surfaces both known tracks', () => {
    expect(formatEpisodeAvailability({
      episodeNumber: 1,
      availableSubtitleLanguageCodes: ['en'],
      availableAudioLanguageCodes: ['en'],
      destinations: [],
    })).toBe('Sub: EN · Dub: EN');
  });

  test('omits a track when no availability is known for it', () => {
    expect(formatEpisodeAvailability({
      episodeNumber: 1,
      availableSubtitleLanguageCodes: ['en'],
      availableAudioLanguageCodes: [],
      destinations: [],
    })).toBe('Sub: EN');
  });

  test('omits the availability label when no episode track is known', () => {
    expect(formatEpisodeAvailability()).toBeNull();
  });

  test('keeps the actual maximum beside language-specific counts', () => {
    expect(formatReleaseAvailability({
      maxReleasedEpisodes: 12,
      languages: [{
        languageCode: 'en',
        subReleasedEpisodes: 12,
        dubReleasedEpisodes: 8,
      }],
    })).toBe('12 episodes released · EN: Sub 12 · Dub 8');
  });
});
