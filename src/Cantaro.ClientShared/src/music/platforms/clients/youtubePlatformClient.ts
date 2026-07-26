import { PlatformPlaylistModel, preloadPlatformPlaylistSongs } from '../playlistModel';
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

interface ApiErrorResponse {
    code?: string;
    error?: string;
}

const reconnectRequiredCode = 'youtube_reconnect_required';
const reconnectRequiredMessage = 'Your YouTube connection expired. Reconnect YouTube to continue browsing playlists.';

class YouTubeReconnectRequiredError extends Error {
    public readonly code = reconnectRequiredCode;

    public constructor(message = reconnectRequiredMessage) {
        super(message);
        this.name = 'YouTubeReconnectRequiredError';
    }
}

export function isYouTubeReconnectRequiredError(error: unknown): error is YouTubeReconnectRequiredError {
    return error instanceof YouTubeReconnectRequiredError;
}

class YouTubePlatformClient implements PlatformManagement {
    public readonly platformId = 'youtube' as const;

    private playlistCache: PlatformPlaylist[] | null = null;
    private readonly playlistSongsCache = new Map<string, PlatformSong[]>();
    private static readonly disconnectErrorMessage = 'Failed to disconnect YouTube account';

    private getHeaders(): HeadersInit {
        return {
            'Content-Type': 'application/json',
        };
    }

    private async createFetchError(response: Response, fallbackMessage: string): Promise<Error> {
        if (response.status === 401) {
            return new Error('Not authenticated. Please log in again.');
        }

        if (response.status === 403) {
            return new Error('YouTube account not connected or access denied.');
        }

        const error = await response.json().catch((): ApiErrorResponse => ({
            error: `${fallbackMessage} (HTTP ${response.status})`,
        }));

        if (response.status === 409 && error.code === reconnectRequiredCode) {
            return new YouTubeReconnectRequiredError(error.error);
        }

        return new Error(error.error || fallbackMessage);
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

    private toDisconnectError(error: unknown): Error {
        return error instanceof Error ? error : new Error(YouTubePlatformClient.disconnectErrorMessage);
    }

    private async disconnectAccount(): Promise<void> {
        const response = await fetch('/api/platforms/youtube/disconnect', {
            method: 'POST',
            headers: this.getHeaders(),
            credentials: 'include',
        }).catch((error) => {
            throw this.toDisconnectError(error);
        });

        if (!response.ok) {
            throw new Error(YouTubePlatformClient.disconnectErrorMessage);
        }
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
            await this.disconnectAccount();
            this.clearCache();
            callbacks?.onSuccess?.();
        } catch (error) {
            const disconnectError = this.toDisconnectError(error);
            callbacks?.onError?.(disconnectError);
            throw disconnectError;
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
            throw await this.createFetchError(response, 'Failed to fetch playlists');
        }

        const playlistDtos = (await response.json()) as PlatformPlaylistDto[];
        const playlists = playlistDtos.map(
            (playlistDto) =>
                new PlatformPlaylistModel(playlistDto, (forceRefresh = false) =>
                    this.songs(playlistDto.id, forceRefresh),
                ),
        );

        this.playlistCache = playlists;
        await preloadPlatformPlaylistSongs(playlists, includeSongs);

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
            throw await this.createFetchError(response, 'Failed to fetch playlist songs');
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
