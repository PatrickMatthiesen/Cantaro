import { PlatformPlaylistModel } from '../playlistModel';
import type {
    PlatformAccountStatus,
    PlatformConnectCallbacks,
    PlatformConnectSource,
    PlatformDisconnectCallbacks,
    PlatformManagement,
    PlatformPlaylist,
    PlatformSong,
} from '../types';

interface PlatformPlaylistDto {
    id: string;
    title: string;
    description?: string;
    thumbnailUrl?: string;
    itemCount: number;
    publishedAt?: string;
}

interface PlatformSongDto {
    id: string;
    title: string;
    description?: string;
    thumbnailUrl?: string;
    artistName?: string;
    index: number;
    publishedAt?: string;
}

class YouTubePlatformClient implements PlatformManagement {
    public readonly platformId = 'youtube' as const;

    private playlistCache: PlatformPlaylist[] | null = null;
    private readonly playlistSongsCache = new Map<string, PlatformSong[]>();

    private getHeaders(): HeadersInit {
        return {
            'Content-Type': 'application/json',
        };
    }

    private sanitizeRoute(route: string): string {
        if (!route || !route.startsWith('/')) {
            return '/';
        }

        if (route.startsWith('//') || route.includes('://') || route.includes('\\')) {
            return '/';
        }

        return route;
    }

    public async status(): Promise<PlatformAccountStatus> {
        const response = await fetch('/api/platforms/youtube/status', {
            method: 'GET',
            headers: this.getHeaders(),
            credentials: 'include',
        });

        if (!response.ok) {
            throw new Error('Failed to get YouTube connection status');
        }

        return response.json();
    }

    public async connect(source: PlatformConnectSource, callbacks?: PlatformConnectCallbacks): Promise<void> {
        callbacks?.onStart?.();

        try {
            const query = new URLSearchParams({
                route: this.sanitizeRoute(source.route),
                trigger: source.trigger,
            });

            window.location.assign(`/api/platforms/youtube/connect?${query.toString()}`);
        } catch (error) {
            callbacks?.onError?.(error instanceof Error ? error : new Error('Failed to start YouTube connect flow'));
            throw error;
        }
    }

    public async disconnect(callbacks?: PlatformDisconnectCallbacks): Promise<void> {
        callbacks?.onStart?.();

        try {
            const response = await fetch('/api/platforms/youtube/disconnect', {
                method: 'POST',
                headers: this.getHeaders(),
                credentials: 'include',
            });

            if (!response.ok) {
                throw new Error('Failed to disconnect YouTube account');
            }

            this.clearCache();
            callbacks?.onSuccess?.();
        } catch (error) {
            callbacks?.onError?.(error instanceof Error ? error : new Error('Failed to disconnect YouTube account'));
            throw error;
        }
    }

    public async playlists(includeSongs = false): Promise<PlatformPlaylist[]> {
        if (this.playlistCache !== null) {
            if (includeSongs) {
                await Promise.all(this.playlistCache.map((playlist) => playlist.songs()));
            }

            return this.playlistCache;
        }

        return this.refreshPlaylists(includeSongs);
    }

    public async refreshPlaylists(includeSongs = false): Promise<PlatformPlaylist[]> {
        this.clearCache();

        const response = await fetch('/api/platforms/youtube/playlists', {
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
                error: `Failed to fetch playlists (HTTP ${response.status})`,
            }));
            throw new Error(error.error || 'Failed to fetch playlists');
        }

        const playlistDtos = (await response.json()) as PlatformPlaylistDto[];
        const playlists = playlistDtos.map(
            (playlistDto) =>
                new PlatformPlaylistModel(playlistDto, (forceRefresh = false) =>
                    this.songs(playlistDto.id, forceRefresh),
                ),
        );

        this.playlistCache = playlists;

        if (includeSongs) {
            await Promise.all(playlists.map((playlist) => playlist.songs()));
        }

        return playlists;
    }

    public async songs(playlistId: string, forceRefresh = false): Promise<PlatformSong[]> {
        if (!forceRefresh && this.playlistSongsCache.has(playlistId)) {
            return this.playlistSongsCache.get(playlistId) ?? [];
        }

        const response = await fetch(`/api/platforms/youtube/playlists/${encodeURIComponent(playlistId)}/songs`, {
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
                error: `Failed to fetch playlist songs (HTTP ${response.status})`,
            }));
            throw new Error(error.error || 'Failed to fetch playlist songs');
        }

        const songDtos = (await response.json()) as PlatformSongDto[];
        this.playlistSongsCache.set(playlistId, songDtos);
        return songDtos;
    }

    public clearCache(): void {
        this.playlistCache = null;
        this.playlistSongsCache.clear();
    }
}

export const youtubePlatformClient = new YouTubePlatformClient();
