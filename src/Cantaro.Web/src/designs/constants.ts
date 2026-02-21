import type { PlatformCard } from "./LayoutShell";

export const platformCatalog: PlatformCard[] = [
  { id: 'youtube', name: 'YouTube Music', status: 'connected', tracks: 1247, icon: '▶', gradient: 'from-red-500 to-pink-500' },
  { id: 'spotify', name: 'Spotify', status: 'available', tracks: 0, icon: '♫', gradient: 'from-green-400 to-emerald-600' },
  { id: 'apple', name: 'Apple Music', status: 'available', tracks: 0, icon: '♫', gradient: 'from-pink-400 to-rose-500' },
  { id: 'tidal', name: 'Tidal', status: 'available', tracks: 0, icon: '◈', gradient: 'from-gray-700 to-gray-900' },
];
