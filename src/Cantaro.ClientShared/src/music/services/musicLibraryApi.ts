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
  playlists: MusicLibrarySongPlaylist[];
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
}

export const musicLibraryApi = new MusicLibraryApiClient();
