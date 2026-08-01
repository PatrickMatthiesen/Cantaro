import type { LyricsResult } from '@cantaro/client-shared/music';

export interface DisplayLyrics {
  text: string;
  synchronized: boolean;
}

function hasText(value: string | undefined): value is string {
  return Boolean(value?.trim());
}

function lyricsWithoutTimestamps(value: string): string {
  return value
    .replace(/\r\n/g, '\n')
    .split('\n')
    .map((line) => line.replace(/^(?:\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\])+[ \t]*/, ''))
    .join('\n')
    .trim();
}

/** Plain lyrics preserve authored stanza breaks; timed lyrics are only a fallback without a player clock. */
export function lyricsForDisplay(result: LyricsResult): DisplayLyrics | null {
  if (hasText(result.plainLyrics)) {
    return {
      synchronized: false,
      text: result.plainLyrics.replace(/\r\n/g, '\n').trim(),
    };
  }

  if (!hasText(result.syncedLyrics)) return null;

  return {
    synchronized: true,
    text: lyricsWithoutTimestamps(result.syncedLyrics),
  };
}
