import { youtubePlatformClient } from './clients/youtubePlatformClient';
import { spotifyPlatformClient } from './clients/spotifyPlatformClient';
import type {
    PlatformAccountStatus,
    PlatformConnectCallbacks,
    PlatformConnectSource,
    PlatformDisconnectCallbacks,
    PlatformId,
    PlatformManagement,
    PlatformPlaylist,
    PlatformSong,
} from './types';

class PlatformManager {
    private readonly clients: Map<PlatformId, PlatformManagement>;

    constructor() {
        this.clients = new Map<PlatformId, PlatformManagement>([
            ['youtube', youtubePlatformClient],
            ['spotify', spotifyPlatformClient],
        ]);
    }

    public getClient(platformId: PlatformId): PlatformManagement {
        const client = this.clients.get(platformId);
        if (!client) {
            throw new Error(`Platform ${platformId} is not implemented`);
        }

        return client;
    }

    public async status(platformId: PlatformId): Promise<PlatformAccountStatus> {
        return this.getClient(platformId).status();
    }

    public async connect(
        platformId: PlatformId,
        source: PlatformConnectSource,
        callbacks?: PlatformConnectCallbacks,
    ): Promise<void> {
        return this.getClient(platformId).connect(source, callbacks);
    }

    public async disconnect(
        platformId: PlatformId,
        callbacks?: PlatformDisconnectCallbacks,
    ): Promise<void> {
        return this.getClient(platformId).disconnect(callbacks);
    }

    public async playlists(platformId: PlatformId, includeSongs = false): Promise<PlatformPlaylist[]> {
        return this.getClient(platformId).playlists(includeSongs);
    }

    public async refreshPlaylists(platformId: PlatformId, includeSongs = false): Promise<PlatformPlaylist[]> {
        return this.getClient(platformId).refreshPlaylists(includeSongs);
    }

    public async songs(platformId: PlatformId, playlistId: string, forceRefresh = false): Promise<PlatformSong[]> {
        return this.getClient(platformId).songs(playlistId, forceRefresh);
    }

    public clearPlatformCache(platformId: PlatformId): void {
        this.getClient(platformId).clearCache();
    }
}

export const platformManager = new PlatformManager();
