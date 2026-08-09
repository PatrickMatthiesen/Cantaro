export interface MusicLibrarySummary {
  songCount: number;
  playlistCount: number;
}

export interface MusicLibrarySongPlaylist {
  playlistId: string;
  playlistName: string;
  position: number;
}

export interface MusicLibrarySong {
  id: string;
  title: string;
  artist?: string;
  artistCredits: MusicLibrarySongArtistCredit[];
  albums: string[];
  thumbnailUrl?: string;
  durationSeconds?: number;
  matchStatus?: string;
  sourcePlatforms: string[];
  sourceIdentities: MusicLibrarySongSourceIdentity[];
  platformLinks: MusicLibrarySongPlatformLink[];
  playlists: MusicLibrarySongPlaylist[];
}

export interface MusicLibrarySongArtistCredit {
  artistId: string;
  name: string;
  creditedName: string;
  role: 'primary' | 'featured' | 'composer' | 'remixer';
  position: number;
  musicBrainzArtistId?: string;
}

export interface MusicLibrarySongSourceIdentity {
  source: string;
  externalId: string;
}

export interface MusicLibrarySongPlatformLink {
  platform: string;
  label: string;
  url: string;
}

export interface MusicLibraryPlaylistService {
  service: string;
  servicePlaylistId: string;
  lastSyncedAt?: string;
  lastSyncStatus?: string;
}

export interface MusicLibraryPlaylist {
  id: string;
  name: string;
  description?: string;
  entryCount: number;
  services: MusicLibraryPlaylistService[];
}

export interface MusicLibraryResponse {
  summary: MusicLibrarySummary;
  songs: MusicLibrarySong[];
  playlists: MusicLibraryPlaylist[];
}

export type LyricsState = 'available' | 'instrumental' | 'unavailable' | 'ambiguous' | 'disabled' | 'provider_error';

export interface LyricsResult {
  state: LyricsState;
  matchStatus: string;
  provider: string;
  providerRecordId?: string;
  plainLyrics?: string;
  syncedLyrics?: string;
  confidence?: number;
  attribution: string;
  explanation?: string;
}

function canonicalTrackId(songId: string): string {
  return songId.startsWith('track:') ? songId.slice(6) : songId;
}

function youtubeVideoQuery(youtubeVideoId?: string): string {
  return youtubeVideoId ? `?youtubeVideoId=${encodeURIComponent(youtubeVideoId)}` : '';
}

function playlistMutationError(method: 'POST' | 'DELETE', body: { error?: string } | null): Error {
  const fallback = method === 'POST' ? 'Could not add this song.' : 'Could not remove this song.';
  return new Error(body?.error ?? fallback);
}

class MusicLibraryApiClient {
  async getLibrary(): Promise<MusicLibraryResponse> {
    const response = await fetch('/api/music/library', {
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: 'Failed to load music library' }));
      throw new Error(error.error || 'Failed to load music library');
    }

    return response.json();
  }

  async getCanonicalSong(songId: string): Promise<MusicLibrarySong> {
    const trackId = canonicalTrackId(songId);
    const response = await fetch(`/api/music/library/songs/${encodeURIComponent(trackId)}`, { credentials: 'include' });
    if (!response.ok) throw new Error('Failed to load song details');
    return response.json() as Promise<MusicLibrarySong>;
  }

  async getLyrics(songId: string, signal?: AbortSignal): Promise<LyricsResult> {
    const trackId = canonicalTrackId(songId);
    const response = await fetch(`/api/music/tracks/${encodeURIComponent(trackId)}/lyrics`, {
      credentials: 'include',
      signal,
    });

    if (response.status === 404) throw new Error('This song is not available in Cantaro.');
    if (!response.ok) throw new Error('Lyrics could not be loaded right now.');
    return response.json() as Promise<LyricsResult>;
  }

  async addSongToPlaylist(songId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
    await this.mutatePlaylistSong('POST', songId, playlistId, youtubeVideoId);
  }

  async removeSongFromPlaylist(songId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
    await this.mutatePlaylistSong('DELETE', songId, playlistId, youtubeVideoId);
  }

  private async mutatePlaylistSong(method: 'POST' | 'DELETE', songId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
    const trackId = canonicalTrackId(songId);
    const query = youtubeVideoQuery(youtubeVideoId);
    const response = await fetch(`/api/music/library/playlists/${encodeURIComponent(playlistId)}/songs/${encodeURIComponent(trackId)}${query}`, {
      method,
      credentials: 'include',
    });
    if (!response.ok) {
      const body = await response.json().catch(() => null) as { error?: string } | null;
      throw playlistMutationError(method, body);
    }
  }
}

export const musicLibraryApi = new MusicLibraryApiClient();
