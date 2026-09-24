import type { LyricsResult } from '../../../app/music/musicLyrics';
import { createCorrelationId, type MessageResult } from '../../../platform/messaging/messageResult';

export interface YouTubeLyricsRequest {
  type: 'music.youtube.lyrics';
  correlationId: string;
  payload: {
    videoId: string;
    search?: { title: string; artist: string };
  };
}

export interface YouTubeLyricsResponse {
  song: { id: string; title: string; artist?: string } | null;
  lyrics: LyricsResult | null;
}

export function youtubeVideoId(url: string): string | null {
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'https:' || !['www.youtube.com', 'music.youtube.com'].includes(parsed.hostname)
      || parsed.pathname !== '/watch') return null;
    const id = parsed.searchParams.get('v');
    return id && /^[a-zA-Z0-9_-]{11}$/.test(id) ? id : null;
  } catch { return null; }
}

export function isYouTubeLyricsRequest(value: unknown): value is YouTubeLyricsRequest {
  if (!value || typeof value !== 'object') return false;
  const request = value as Partial<YouTubeLyricsRequest>;
  return request.type === 'music.youtube.lyrics' && typeof request.correlationId === 'string'
    && typeof request.payload?.videoId === 'string' && /^[a-zA-Z0-9_-]{11}$/.test(request.payload.videoId)
    && (request.payload.search === undefined || isManualSearch(request.payload.search));
}

function isManualSearch(value: unknown): value is { title: string; artist: string } {
  if (typeof value !== 'object' || value === null) return false;
  const search = value as { title?: unknown; artist?: unknown };
  return typeof search.title === 'string' && search.title.trim().length > 0 && search.title.length <= 200
    && typeof search.artist === 'string' && search.artist.length <= 200;
}

export async function requestYouTubeLyrics(
  videoId: string,
  search?: { title: string; artist: string },
): Promise<YouTubeLyricsResponse> {
  const result = await browser.runtime.sendMessage({
    type: 'music.youtube.lyrics', correlationId: createCorrelationId(), payload: { videoId, ...(search ? { search } : {}) },
  } satisfies YouTubeLyricsRequest) as MessageResult<YouTubeLyricsResponse>;
  if (!result?.ok) throw new Error(result?.error.message ?? 'Cantaro could not load lyrics. Try again.');
  return result.value;
}
