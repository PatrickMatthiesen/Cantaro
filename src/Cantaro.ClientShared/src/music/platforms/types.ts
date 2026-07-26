export type PlatformId = 'youtube' | 'spotify' | 'apple' | 'tidal';

export interface PlatformConnectSource {
    route: string;
    trigger: string;
}

export interface PlatformConnectCallbacks {
    onStart?: () => void;
    onError?: (error: Error) => void;
}

export interface PlatformDisconnectCallbacks {
    onStart?: () => void;
    onSuccess?: () => void;
    onError?: (error: Error) => void;
}

export interface PlatformAccountStatus {
    platformId: PlatformId;
    isConnected: boolean;
    displayName?: string;
    externalAccountId?: string;
    connectedAt?: string;
    connectionState?: 'connected' | 'reconnect_required' | 'disconnected';
    needsReconnect?: boolean;
}

export interface PlatformSong {
    id: string;
    title: string;
    description?: string;
    thumbnailUrl?: string;
    artistName?: string;
    index: number;
    publishedAt?: string;
    durationSeconds?: number;
    externalUrl?: string;
    albumName?: string;
    albumUrl?: string;
    artistUrl?: string;
}

export interface PlatformPlaylist {
    id: string;
    title: string;
    description?: string;
    thumbnailUrl?: string;
    itemCount: number;
    publishedAt?: string;
    externalUrl?: string;
    ownerName?: string;
    songs: (forceRefresh?: boolean) => Promise<PlatformSong[]>;
}

export interface PlatformManagement {
    readonly platformId: PlatformId;
    status: () => Promise<PlatformAccountStatus>;
    connect: (source: PlatformConnectSource, callbacks?: PlatformConnectCallbacks) => Promise<void>;
    disconnect: (callbacks?: PlatformDisconnectCallbacks) => Promise<void>;
    playlists: (includeSongs?: boolean) => Promise<PlatformPlaylist[]>;
    refreshPlaylists: (includeSongs?: boolean) => Promise<PlatformPlaylist[]>;
    songs: (playlistId: string, forceRefresh?: boolean) => Promise<PlatformSong[]>;
    clearCache: () => void;
}
