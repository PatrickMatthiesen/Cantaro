import { describe, expect, it } from 'vitest';
import { lyricsSearchHints } from './searchHints';
describe('local search suggestions', () => {
  it('removes a leading tab notification count and song qualifiers', () => {
    expect(lyricsSearchHints('(15) Jon Bellion - BLU (Live from Forest Hills) - YouTube'))
      .toEqual({ artist: 'Jon Bellion', title: 'BLU' });
    expect(lyricsSearchHints('Artist - Song (2024) - YouTube'))
      .toEqual({ artist: 'Artist', title: 'Song' });
  });
  it('removes multiple and nested parenthesized qualifiers from title suggestions', () => {
    expect(lyricsSearchHints('Artist - Song (Live (Acoustic)) (2024) - YouTube'))
      .toEqual({ artist: 'Artist', title: 'Song' });
    expect(lyricsSearchHints('Song (Official Audio) - YouTube'))
      .toEqual({ artist: '', title: 'Song' });
  });
  it('splits an artist and song without querying a provider', () => {
    expect(lyricsSearchHints('Jon Bellion - Maybe IDK - YouTube')).toEqual({ artist: 'Jon Bellion', title: 'Maybe IDK' });
  });
  it('does not invent an artist for ambiguous titles', () => {
    expect(lyricsSearchHints('A - B - C - YouTube')).toEqual({ artist: '', title: 'A - B - C' });
    expect(lyricsSearchHints('Song - YouTube Music')).toEqual({ artist: '', title: 'Song' });
  });
});
