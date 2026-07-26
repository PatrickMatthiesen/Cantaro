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
import { createPlatformApiError } from './platformApiError';

type PlatformPlaylistDto = Omit<PlatformPlaylist, 'songs'>;
type PlatformSongDto = PlatformSong;

class SpotifyPlatformClient implements PlatformManagement {
    public readonly platformId = 'spotify' as const;
    private playlistCache: PlatformPlaylist[] | null = null;
    private readonly playlistSongsCache = new Map<string, PlatformSong[]>();

    private async request<T>(path: string, fallbackMessage: string, init?: RequestInit): Promise<T> {
        const response = await fetch(path, {
            credentials: 'include',
            ...init,
            headers: {
                'Content-Type': 'application/json',
                ...init?.headers,
            },
        });

        if (!response.ok) {
            throw await createPlatformApiError('spotify', response, fallbackMessage);
        }

        if (response.status === 204) {
            return undefined as T;
        }

        return response.json() as Promise<T>;
    }

    private sanitizeRoute(route: string): string {
        return route.startsWith('/') && !route.startsWith('//') && !route.includes('://') && !route.includes('\\')
            ? route
            : '/music/platforms/spotify';
    }

    public status(): Promise<PlatformAccountStatus> {
        return this.request('/api/platforms/spotify/status', 'Could not check the Spotify connection.');
    }

    public async connect(source: PlatformConnectSource, callbacks?: PlatformConnectCallbacks): Promise<void> {
        callbacks?.onStart?.();
        try {
            const route = this.sanitizeRoute(source.route);
            const query = new URLSearchParams({ route, trigger: source.trigger }).toString();
            window.location.assign(`/api/platforms/spotify/connect?${query}`);
        } catch (error) {
            const connectionError = error instanceof Error ? error : new Error('Could not start Spotify authorization.');
            callbacks?.onError?.(connectionError);
            throw connectionError;
        }
    }

    public async disconnect(callbacks?: PlatformDisconnectCallbacks): Promise<void> {
        callbacks?.onStart?.();
        try {
            await this.request<{ message?: string }>(
                '/api/platforms/spotify/disconnect',
                'Could not disconnect Spotify. Try again.',
                { method: 'POST' },
            );
            this.clearCache();
            callbacks?.onSuccess?.();
        } catch (error) {
            const disconnectError = error instanceof Error ? error : new Error('Could not disconnect Spotify. Try again.');
            callbacks?.onError?.(disconnectError);
            throw disconnectError;
        }
    }

    public async playlists(includeSongs = false): Promise<PlatformPlaylist[]> {
        if (this.playlistCache === null) {
            return this.refreshPlaylists(includeSongs);
        }

        if (includeSongs) {
            await Promise.all(this.playlistCache.map((playlist) => playlist.songs()));
        }
        return this.playlistCache;
    }

    public async refreshPlaylists(includeSongs = false): Promise<PlatformPlaylist[]> {
        this.clearCache();
        const playlistDtos = await this.request<PlatformPlaylistDto[]>(
            '/api/platforms/spotify/playlists',
            'Could not load Spotify playlists.',
        );
        const playlists: PlatformPlaylist[] = [];
        for (const playlist of playlistDtos) {
            playlists.push(
                new PlatformPlaylistModel(
                    playlist,
                    (forceRefresh = false) => this.songs(playlist.id, forceRefresh),
                ),
            );
        }
        this.playlistCache = playlists;
        await preloadPlatformPlaylistSongs(playlists, includeSongs);
        return playlists;
    }

    public async songs(playlistId: string, forceRefresh = false): Promise<PlatformSong[]> {
        const cachedSongs = this.playlistSongsCache.get(playlistId);
        if (!forceRefresh && cachedSongs) {
            return cachedSongs;
        }

        const encodedPlaylistId = encodeURIComponent(playlistId);
        const path = `/api/platforms/spotify/playlists/${encodedPlaylistId}/songs`;
        const songs = await this.request<PlatformSongDto[]>(path, 'Could not load the tracks in this Spotify playlist.');
        this.playlistSongsCache.set(playlistId, songs);
        return songs;
    }

    public clearCache(): void {
        this.playlistCache = null;
        this.playlistSongsCache.clear();
    }
}

export const spotifyPlatformClient = new SpotifyPlatformClient();
