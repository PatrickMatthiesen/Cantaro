import { PlatformPlaylistModel } from '../playlistModel';
import type {
    PlatformAccountStatus,
    PlatformConnectCallbacks,
    PlatformConnectSource,
    PlatformDisconnectCallbacks,
    PlatformId,
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

class PlatformApiClient implements PlatformManagement {
    public readonly platformId: PlatformId;

    private playlistCache: PlatformPlaylist[] | null = null;
    private readonly playlistSongsCache = new Map<string, PlatformSong[]>();
    private readonly displayName: string;

    constructor(
        platformId: PlatformId,
        displayName: string,
    ) {
        this.platformId = platformId;
        this.displayName = displayName;
    }

    private getHeaders(): HeadersInit {
        return {
            'Content-Type': 'application/json',
        };
    }

    private async readFetchError(response: Response, fallbackMessage: string): Promise<string> {
        if (response.status === 401) {
            return 'Not authenticated. Please log in again.';
        }

        if (response.status === 403) {
            return `${this.displayName} account not connected or access denied.`;
        }

        const error = await response.json().catch(() => ({
            error: `${fallbackMessage} (HTTP ${response.status})`,
        }));

        return error.error || fallbackMessage;
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
        return error instanceof Error ? error : new Error(`Failed to disconnect ${this.displayName} account`);
    }

    private async disconnectAccount(): Promise<void> {
        const response = await fetch(`/api/platforms/${this.platformId}/disconnect`, {
            method: 'POST',
            headers: this.getHeaders(),
            credentials: 'include',
        }).catch((error) => {
            throw this.toDisconnectError(error);
        });

        if (!response.ok) {
            throw new Error(`Failed to disconnect ${this.displayName} account`);
        }
    }

    public async status(): Promise<PlatformAccountStatus> {
        const response = await fetch(`/api/platforms/${this.platformId}/status`, {
            method: 'GET',
            headers: this.getHeaders(),
            credentials: 'include',
        });

        if (!response.ok) {
            throw new Error(`Failed to get ${this.displayName} connection status`);
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

            window.location.assign(`/api/platforms/${this.platformId}/connect?${query.toString()}`);
        } catch (error) {
            callbacks?.onError?.(error instanceof Error ? error : new Error(`Failed to start ${this.displayName} connect flow`));
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

        const response = await fetch(`/api/platforms/${this.platformId}/playlists`, {
            method: 'GET',
            headers: this.getHeaders(),
            credentials: 'include',
        });

        if (!response.ok) {
            throw new Error(await this.readFetchError(response, 'Failed to fetch playlists'));
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

        const response = await fetch(`/api/platforms/${this.platformId}/playlists/${encodeURIComponent(playlistId)}/songs`, {
            method: 'GET',
            headers: this.getHeaders(),
            credentials: 'include',
        });

        if (!response.ok) {
            throw new Error(await this.readFetchError(response, 'Failed to fetch playlist songs'));
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

export function createPlatformApiClient(platformId: PlatformId, displayName: string): PlatformManagement {
    return new PlatformApiClient(platformId, displayName);
}
