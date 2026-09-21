import { describe, expect, test } from 'bun:test';
import {
  formatEpisodeAvailability,
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
});
