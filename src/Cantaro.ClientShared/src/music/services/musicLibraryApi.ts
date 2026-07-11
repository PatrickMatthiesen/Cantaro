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
  albums: string[];
  thumbnailUrl?: string;
  durationSeconds?: number;
  matchStatus?: string;
  sourcePlatforms: string[];
  sourceIdentities: MusicLibrarySongSourceIdentity[];
  platformLinks: MusicLibrarySongPlatformLink[];
  playlists: MusicLibrarySongPlaylist[];
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
    const trackId = songId.startsWith('track:') ? songId.slice(6) : songId;
    const response = await fetch(`/api/music/library/songs/${encodeURIComponent(trackId)}`, { credentials: 'include' });
    if (!response.ok) throw new Error('Failed to load song details');
    return response.json() as Promise<MusicLibrarySong>;
  }

  async addSongToPlaylist(songId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
    await this.mutatePlaylistSong('POST', songId, playlistId, youtubeVideoId);
  }

  async removeSongFromPlaylist(songId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
    await this.mutatePlaylistSong('DELETE', songId, playlistId, youtubeVideoId);
  }

  private async mutatePlaylistSong(method: 'POST' | 'DELETE', songId: string, playlistId: string, youtubeVideoId?: string): Promise<void> {
    const trackId = songId.startsWith('track:') ? songId.slice(6) : songId;
    const query = youtubeVideoId ? `?youtubeVideoId=${encodeURIComponent(youtubeVideoId)}` : '';
    const response = await fetch(`/api/music/library/playlists/${encodeURIComponent(playlistId)}/songs/${encodeURIComponent(trackId)}${query}`, {
      method,
      credentials: 'include',
    });
    if (!response.ok) {
      const body = await response.json().catch(() => null) as { error?: string } | null;
      throw new Error(body?.error ?? `Could not ${method === 'POST' ? 'add' : 'remove'} this song.`);
    }
  }
}

export const musicLibraryApi = new MusicLibraryApiClient();
