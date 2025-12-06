export interface ConnectedAccountStatus {
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
  private getHeaders(): HeadersInit {
    return {
      'Content-Type': 'application/json',
    };
  }

  async getStatus(): Promise<ConnectedAccountStatus> {
    const response = await fetch('/api/youtube/status', {
      method: 'GET',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error('Failed to get YouTube connection status');
    }

    return response.json();
  }

  getConnectUrl(returnUrl?: string): string {
    const params = new URLSearchParams();
    if (returnUrl) {
      params.set('returnUrl', returnUrl);
    }
    return `/api/youtube/connect${params.toString() ? '?' + params.toString() : ''}`;
  }

  async disconnect(): Promise<void> {
    const response = await fetch('/api/youtube/disconnect', {
      method: 'POST',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error('Failed to disconnect YouTube account');
    }
  }

  async getPlaylists(): Promise<YouTubePlaylist[]> {
    const response = await fetch('/api/youtube/playlists', {
      method: 'GET',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      if (response.status === 401) {
        throw new Error('Not authenticated. Please log in again.');
      }
      if (response.status === 403) {
        throw new Error('YouTube account not connected or access denied.');
      }
      const error = await response.json().catch(() => ({ 
        error: `Failed to fetch playlists (HTTP ${response.status})` 
      }));
      throw new Error(error.error || 'Failed to fetch playlists');
    }

    return response.json();
  }

  async getPlaylistItems(playlistId: string): Promise<YouTubePlaylistItem[]> {
    const response = await fetch(`/api/youtube/playlists/${encodeURIComponent(playlistId)}/items`, {
      method: 'GET',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      if (response.status === 401) {
        throw new Error('Not authenticated. Please log in again.');
      }
      if (response.status === 403) {
        throw new Error('YouTube account not connected or access denied.');
      }
      const error = await response.json().catch(() => ({ 
        error: `Failed to fetch playlist items (HTTP ${response.status})` 
      }));
      throw new Error(error.error || 'Failed to fetch playlist items');
    }

    return response.json();
  }
}

export const youtubeApi = new YouTubeApiClient();
