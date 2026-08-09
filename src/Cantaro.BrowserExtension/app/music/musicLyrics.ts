export interface LyricsResult {
  state: 'available' | 'instrumental' | 'unavailable' | 'ambiguous' | 'disabled' | 'provider_error';
  matchStatus: string;
  provider: string;
  providerRecordId?: string;
  plainLyrics?: string;
  syncedLyrics?: string;
  confidence?: number;
  attribution: string;
  explanation?: string;
}

function hasText(value: string | undefined): value is string {
  return Boolean(value?.trim());
}

export function displayLyricsText(result: LyricsResult): { text: string; synchronized: boolean } | null {
  const synchronized = hasText(result.syncedLyrics);
  const text = synchronized ? result.syncedLyrics : result.plainLyrics;
  if (!hasText(text)) return null;
  return {
    synchronized,
    text: synchronized
      ? text.replace(/^\s*(?:\[\d{1,2}:\d{2}(?:\.\d{1,3})?\]\s*)+/gm, '').trim()
      : text,
  };
}
