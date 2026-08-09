import { cantaroApiClient } from '../../../../../platform/api/cantaroApiClient';
import type { YouTubePageContext } from './youtubePage';

export interface MusicRecognitionResult {
  classification: 'music' | 'not_music' | 'uncertain';
  status: string;
  trackId?: string;
  title?: string;
  artist?: string;
  inUserLibrary: boolean;
}

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

export function recognizeYouTubeTrack(
  context: YouTubePageContext,
  signal: AbortSignal,
): Promise<MusicRecognitionResult> {
  return cantaroApiClient.request<MusicRecognitionResult>('/api/music/recognition/youtube', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      videoId: context.externalId,
      site: context.site,
      pageTitle: context.title,
    }),
    signal,
  });
}

export function loadTrackLyrics(
  trackId: string,
  signal: AbortSignal,
): Promise<LyricsResult> {
  return cantaroApiClient.request<LyricsResult>(
    `/api/music/tracks/${encodeURIComponent(trackId)}/lyrics`,
    { signal },
  );
}
