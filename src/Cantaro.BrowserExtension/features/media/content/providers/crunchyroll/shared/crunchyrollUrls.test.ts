import { describe, expect, it } from 'vitest';
import {
  extractEpisodeId,
  extractEpisodeNumber,
  extractSeasonNumber,
  extractSeriesId,
  parseCrunchyrollUrl,
} from './crunchyrollUrls';

describe('Crunchyroll URL parsing', () => {
  it('extracts stable provider identifiers and numbering', () => {
    expect(extractEpisodeId('/watch/G123ABC/slug')).toBe('G123ABC');
    expect(extractSeriesId('/series/S987XYZ/slug')).toBe('S987XYZ');
    expect(extractEpisodeNumber('Play Episode 17 - Return')).toBe(17);
    expect(extractEpisodeNumber("Play Episode 13.5 - Lucy's Diary")).toBeNull();
    expect(extractSeasonNumber('Season 4')).toBe(4);
  });

  it('accepts only HTTPS Crunchyroll destinations', () => {
    expect(parseCrunchyrollUrl('/watch/G123ABC', 'https://www.crunchyroll.com/series/S987XYZ')?.href)
      .toBe('https://www.crunchyroll.com/watch/G123ABC');
    expect(parseCrunchyrollUrl('https://crunchyroll.example/watch/G123ABC')).toBeNull();
    expect(parseCrunchyrollUrl('http://www.crunchyroll.com/watch/G123ABC')).toBeNull();
  });
});
