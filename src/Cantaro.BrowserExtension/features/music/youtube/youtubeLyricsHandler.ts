import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { createCantaroApiClient, type CantaroApiClient } from '../../../platform/api/cantaroApiClient';
import { browserAuthService } from '../../../platform/auth/authService';
import { browserConsentService } from '../../../platform/consent/consentService';
import { browserSettingsRepository } from '../../../platform/settings/settingsRepository';
import { messageFailure, messageSuccess } from '../../../platform/messaging/messageResult';
import { getLocalLyrics } from '../musicLyricsProvider';
import { youtubeVideoId, type YouTubeLyricsRequest, type YouTubeLyricsResponse } from './youtubeLyrics';

async function requireMusicConsent(): Promise<string> {
  const settings = await browserSettingsRepository.read();
  const consent = await browserConsentService.getStatus(settings.baseUrl);
  if (!consent.authenticated || !consent.musicLyricsAllowed) {
    throw new Error('Sign in and enable YouTube lyrics in Cantaro settings.');
  }
  return settings.baseUrl;
}

const lyricsApi = createCantaroApiClient(browserSettingsRepository, browserAuthService, async (_path, baseUrl) => {
  if (await requireMusicConsent() !== baseUrl) throw new Error('Cantaro server changed. Try again.');
});

export function createYouTubeLyricsHandler(
  api: Pick<CantaroApiClient, 'request'>,
  requireConsent: () => Promise<string>,
  fetchLyrics: typeof getLocalLyrics = getLocalLyrics,
  getTab: (id: number) => Promise<{ url?: string }> = id => browser.tabs.get(id),
) {
  return async (request: YouTubeLyricsRequest, sender: Browser.runtime.MessageSender) => {
    if (!await matchesCurrentPage(sender, request.payload.videoId, getTab)) {
      return messageFailure(request.correlationId, 'invalid_request', 'The video does not match the requesting YouTube page.');
    }
    try {
      const baseUrl = await requireConsent();
      const search = request.payload.search;
      if (search) {
        const title = search.title.trim();
        const artist = search.artist.trim();
        const manualSong: MusicLibrarySong = {
          id: 'manual', title, artist, artistCredits: [], albums: [], sourcePlatforms: [],
          sourceIdentities: [], platformLinks: [], playlists: [],
        };
        const lyrics = await fetchLyrics(manualSong);
        if (await requireConsent() !== baseUrl) throw new Error('Cantaro server changed. Try again.');
        return messageSuccess<YouTubeLyricsResponse>({ song: { id: 'manual', title, artist }, lyrics }, request.correlationId);
      }
      const library = await api.request<MusicLibraryResponse>('/api/music/library');
      if (await requireConsent() !== baseUrl) throw new Error('Cantaro server changed. Try again.');
      const matches = library.songs.filter(song => song.id.startsWith('track:')
        && song.sourceIdentities.some(identity => identity.source === 'youtube' && identity.externalId === request.payload.videoId));
      const song = matches.length === 1 ? matches[0] : null;
      if (!song) return messageSuccess<YouTubeLyricsResponse>({ song: null, lyrics: null }, request.correlationId);
      const lyrics = await fetchLyrics(song);
      if (await requireConsent() !== baseUrl) throw new Error('Cantaro server changed. Try again.');
      return messageSuccess<YouTubeLyricsResponse>({ song: { id: song.id, title: song.title, artist: song.artist }, lyrics }, request.correlationId);
    } catch {
      // Do not return backend diagnostics or account details to a website.
      return messageFailure(request.correlationId, 'delivery_failed', 'Could not load lyrics. Check your Cantaro connection and YouTube lyrics setting, then retry.', true);
    }
  };
}

async function matchesCurrentPage(
  sender: Browser.runtime.MessageSender,
  videoId: string,
  getTab: (id: number) => Promise<{ url?: string }>,
): Promise<boolean> {
  if (!sender.tab) return true; // Requests from the extension popup.
  if (sender.frameId !== 0 || sender.tab.id === undefined) return false;
  try {
    const origin = new URL(sender.url ?? '').origin;
    if (origin !== 'https://www.youtube.com' && origin !== 'https://music.youtube.com') return false;
    // The sender document URL can predate YouTube's same-document navigation.
    // Read the current URL from the browser, never from the message payload.
    const tab = await getTab(sender.tab.id);
    return youtubeVideoId(tab.url ?? '') === videoId;
  } catch {
    return false; // Closed tabs and unavailable URLs must not authorize a lookup.
  }
}

export const handleYouTubeLyrics = createYouTubeLyricsHandler(lyricsApi, requireMusicConsent);
