import type { PlatformId } from './types';

export interface PlatformCatalogEntry {
    id: PlatformId;
    name: string;
    iconId: PlatformId;
    gradient: string;
    implemented: boolean;
}

export const platformCatalog: PlatformCatalogEntry[] = [
    { id: 'youtube', name: 'YouTube Music', iconId: 'youtube', gradient: 'from-red-500 to-pink-500', implemented: true },
    { id: 'spotify', name: 'Spotify', iconId: 'spotify', gradient: 'from-green-400 to-emerald-600', implemented: true },
    { id: 'apple', name: 'Apple Music', iconId: 'apple', gradient: 'from-pink-400 to-rose-500', implemented: false },
    { id: 'tidal', name: 'Tidal', iconId: 'tidal', gradient: 'from-gray-700 to-gray-900', implemented: false },
];
