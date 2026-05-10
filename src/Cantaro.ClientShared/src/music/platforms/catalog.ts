import type { PlatformId } from './types';

export interface PlatformCatalogEntry {
    id: PlatformId;
    name: string;
    icon: string;
    gradient: string;
    implemented: boolean;
}

export const platformCatalog: PlatformCatalogEntry[] = [
    { id: 'youtube', name: 'YouTube Music', icon: '▶', gradient: 'from-red-500 to-pink-500', implemented: true },
    { id: 'spotify', name: 'Spotify', icon: '♫', gradient: 'from-green-400 to-emerald-600', implemented: false },
    { id: 'apple', name: 'Apple Music', icon: '◉', gradient: 'from-pink-400 to-rose-500', implemented: false },
    { id: 'tidal', name: 'Tidal', icon: '◈', gradient: 'from-gray-700 to-gray-900', implemented: false },
];