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

  it('displays plain lyrics', () => {
    expect(displayLyricsText(result({ plainLyrics: 'Words' }))).toEqual({ synchronized: false, text: 'Words' });
  });

  it('prefers plain lyrics and preserves their verse spacing exactly', () => {
    const plainLyrics = 'First line\nSecond line\n\nNext verse\n';
    expect(displayLyricsText(result({ plainLyrics, syncedLyrics: '[00:01]First line\n[00:02]Second line\n[00:03]Next verse' })))
      .toEqual({ synchronized: false, text: plainLyrics });
  });

  it.each(['\n', '\r\n'])('preserves blank lines and timestamp-only lines with newline %j', newline => {
    const syncedLyrics = ['[00:01] First', '[00:02.000]', '', '[00:03][00:04.50] Second', ''].join(newline);
    expect(displayLyricsText(result({ plainLyrics: '  ', syncedLyrics })))
      .toEqual({ synchronized: true, text: ['First', '', '', 'Second', ''].join(newline) });
  });

});
