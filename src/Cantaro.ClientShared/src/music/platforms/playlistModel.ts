import type { PlatformPlaylist, PlatformSong } from './types';

interface PlatformPlaylistModelInput {
    id: string;
    title: string;
    description?: string;
    thumbnailUrl?: string;
    itemCount: number;
    publishedAt?: string;
    externalUrl?: string;
    ownerName?: string;
}

export class PlatformPlaylistModel implements PlatformPlaylist {
    public readonly id: string;
    public readonly title: string;
    public readonly description?: string;
    public readonly thumbnailUrl?: string;
    public readonly itemCount: number;
    public readonly publishedAt?: string;
    public readonly externalUrl?: string;
    public readonly ownerName?: string;

    private songsCache: PlatformSong[] | null;
    private readonly loadSongs: (forceRefresh?: boolean) => Promise<PlatformSong[]>;

    constructor(
        input: PlatformPlaylistModelInput,
        loadSongs: (forceRefresh?: boolean) => Promise<PlatformSong[]>,
        initialSongs?: PlatformSong[],
    ) {
        this.id = input.id;
        this.title = input.title;
        this.description = input.description;
        this.thumbnailUrl = input.thumbnailUrl;
        this.itemCount = input.itemCount;
        this.publishedAt = input.publishedAt;
        this.externalUrl = input.externalUrl;
        this.ownerName = input.ownerName;
        this.loadSongs = loadSongs;
        this.songsCache = initialSongs ?? null;
    }

    public async songs(forceRefresh = false): Promise<PlatformSong[]> {
        if (this.songsCache !== null && !forceRefresh) {
            return this.songsCache;
        }

        const loadedSongs = await this.loadSongs(forceRefresh);
        this.songsCache = loadedSongs;
        return loadedSongs;
    }
}

export async function preloadPlatformPlaylistSongs(
    playlists: PlatformPlaylist[],
    includeSongs: boolean,
): Promise<void> {
    if (includeSongs) {
        await Promise.all(playlists.map((playlist) => playlist.songs()));
    }
}
