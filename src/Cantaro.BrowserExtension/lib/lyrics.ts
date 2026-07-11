import { ensureExtensionAccessToken } from './cantaroAuthSession';
import { readExtensionConfig } from './extensionRuntimeConfig';

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

interface CachedLyrics {
  expiresAt: number;
  result: LyricsResult;
}

const LYRICS_SESSION_CACHE_TTL_MS = 5 * 60 * 1000;

// Deliberately process-local: neither lyric text nor lookup metadata is persisted to extension storage.
const sessionLyricsCache = new Map<string, CachedLyrics>();

function hasText(value: string | undefined): value is string {
  return Boolean(value?.trim());
}

function cacheKey(apiBaseUrl: string, sessionEmail: string, trackId: string): string {
  return `${apiBaseUrl}|${sessionEmail}|${trackId}`;
}

/**
 * All lyrics requests go through Cantaro. This keeps provider credentials, matching,
 * caching, and attribution in one auditable place rather than exposing a third-party
 * lyrics service to page scripts.
 */
export async function getCanonicalTrackLyrics(trackId: string, signal?: AbortSignal): Promise<LyricsResult> {
  const config = await readExtensionConfig();
  const token = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!token) throw new Error('Sign in to Cantaro to view lyrics.');

  const response = await fetch(`${config.apiBaseUrl}/api/music/tracks/${encodeURIComponent(trackId)}/lyrics`, {
    headers: { Authorization: `Bearer ${token}` },
    signal,
  });

  if (!response.ok) {
    if (response.status === 404) throw new Error('This song is not available in Cantaro.');
    if (response.status === 401) throw new Error('Your Cantaro session expired. Sign in again.');
    throw new Error('Lyrics could not be loaded right now.');
  }

  return response.json() as Promise<LyricsResult>;
}

/** Returns timed lyrics when available; the popup does not have a player clock, so LRC markers are omitted. */
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

/**
 * Popup-specific lyrics client. It caches successful provider responses only for the current
 * extension session and signed-in identity, avoiding repeated requests while a user browses songs.
 */
export async function loadExtensionLyrics(trackId: string, signal?: AbortSignal): Promise<LyricsResult> {
  const config = await readExtensionConfig();
  const token = await ensureExtensionAccessToken(config.apiBaseUrl);
  if (!token) throw new Error('Sign in to view lyrics.');

  const key = cacheKey(config.apiBaseUrl, config.sessionEmail, trackId);
  const cached = sessionLyricsCache.get(key);
  if (cached && cached.expiresAt > Date.now()) return cached.result;
  if (cached) sessionLyricsCache.delete(key);

  const response = await fetch(`${config.apiBaseUrl}/api/music/tracks/${encodeURIComponent(trackId)}/lyrics`, {
    headers: { Authorization: `Bearer ${token}` },
    signal,
  });

  if (!response.ok) {
    if (response.status === 404) {
      return {
        state: 'unavailable',
        matchStatus: 'unavailable',
        provider: 'Cantaro',
        attribution: 'Lyrics lookup by Cantaro',
        explanation: 'Lyrics are not available for this song yet.',
      };
    }
    if (response.status === 401) throw new Error('Your Cantaro session expired. Sign in again.');
    throw new Error('The lyrics provider could not be reached.');
  }

  const result = await response.json() as LyricsResult;
  if (result.state !== 'provider_error') {
    sessionLyricsCache.set(key, { result, expiresAt: Date.now() + LYRICS_SESSION_CACHE_TTL_MS });
  }
  return result;
}

export function clearLyricsSessionCache(): void {
  sessionLyricsCache.clear();
}
