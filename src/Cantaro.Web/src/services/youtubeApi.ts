import { platformManager } from '../platforms';

export interface ConnectedAccountStatus {
  platformId?: string;
  isConnected: boolean;
  displayName?: string;
  externalAccountId?: string;
  connectedAt?: string;
}

export interface YouTubePlaylist {
  id: string;
  title: string;
  description?: string;
  thumbnailUrl?: string;
  itemCount: number;
  publishedAt?: string;
}

export interface YouTubePlaylistItem {
  videoId: string;
  title: string;
  description?: string;
  thumbnailUrl?: string;
  channelTitle?: string;
  position: number;
  publishedAt?: string;
}

class YouTubeApiClient {
  private readonly youtubeClient = platformManager.getClient('youtube');

  async getStatus(): Promise<ConnectedAccountStatus> {
    return this.youtubeClient.status();
  }

  getConnectUrl(returnUrl?: string): string {
    const params = new URLSearchParams();
    if (returnUrl) {
      try {
        const parsed = new URL(returnUrl);
        params.set('route', parsed.pathname || '/youtube');
      } catch {
        params.set('route', returnUrl);
      }
    }
    params.set('trigger', 'legacy-youtube-api');
    return `/api/platforms/youtube/connect${params.toString() ? '?' + params.toString() : ''}`;
  }

  async disconnect(): Promise<void> {
    await this.youtubeClient.disconnect();
  }

  async getPlaylists(): Promise<YouTubePlaylist[]> {
    const playlists = await this.youtubeClient.playlists(false);
    return playlists.map((playlist) => ({
      id: playlist.id,
      title: playlist.title,
      description: playlist.description,
      thumbnailUrl: playlist.thumbnailUrl,
      itemCount: playlist.itemCount,
      publishedAt: playlist.publishedAt,
    }));
  }

  async getPlaylistItems(playlistId: string): Promise<YouTubePlaylistItem[]> {
    const songs = await this.youtubeClient.songs(playlistId);
    return songs.map((song) => ({
      videoId: song.id,
      title: song.title,
      description: song.description,
      thumbnailUrl: song.thumbnailUrl,
      channelTitle: song.artistName,
      position: song.index,
      publishedAt: song.publishedAt,
    }));
  }
}

export const youtubeApi = new YouTubeApiClient();
