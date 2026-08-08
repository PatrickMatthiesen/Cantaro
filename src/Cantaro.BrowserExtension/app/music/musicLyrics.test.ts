import { describe, expect, it } from 'vitest';
import { displayLyricsText, type LyricsResult } from './musicLyrics';

function result(overrides: Partial<LyricsResult>): LyricsResult {
  return {
    state: 'available',
    matchStatus: 'exact',
    provider: 'test',
    attribution: 'test',
    ...overrides,
  };
}

describe('displayLyricsText', () => {
  it('removes time markers from synchronized lyrics', () => {
    expect(displayLyricsText(result({ syncedLyrics: '[00:10.00] First\n[00:12] Second' }))).toEqual({
      synchronized: true,
      text: 'First\nSecond',
    });
  });

  it('falls back to plain lyrics', () => {
    expect(displayLyricsText(result({ plainLyrics: 'Words' }))).toEqual({ synchronized: false, text: 'Words' });
  });
});
