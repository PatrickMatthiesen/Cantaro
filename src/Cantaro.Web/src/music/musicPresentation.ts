import { platformCatalog, type MusicLibraryPlaylist, type MusicLibrarySong, type PlatformId } from '@cantaro/client-shared/music';

const fallbackArtwork = [
  'https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1516280440614-37939bbacd81?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1511379938547-c1f69419868d?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1524650359799-842906ca1c06?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1458560871784-56d23406c091?auto=format&fit=crop&w=640&q=80',
  'https://images.unsplash.com/photo-1508700115892-45ecd05ae2ad?auto=format&fit=crop&w=640&q=80',
];

export const playlistGradients = [
  'from-[#ff9a8b] via-[#ff6a88] to-[#8054ff]',
  'from-[#ffd166] via-[#8bd3dd] to-[#3d5afe]',
  'from-[#6ee7b7] via-[#60a5fa] to-[#7c3aed]',
  'from-[#fca5a5] via-[#fdba74] to-[#92400e]',
  'from-[#93c5fd] via-[#c4b5fd] to-[#312e81]',
  'from-[#f0abfc] via-[#c084fc] to-[#be123c]',
];

export function formatDuration(seconds?: number): string {
  if (!seconds) return '0:00';
  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

export function formatTimestamp(value?: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

export function formatRelativeTime(value?: string | null, emptyLabel = 'Never synced'): string {
  if (!value) return emptyLabel;

  const date = new Date(value);
  const diffMs = Date.now() - date.getTime();
  const diffMinutes = Math.max(0, Math.round(diffMs / 60000));

  if (diffMinutes < 1) return 'Just now';
  if (diffMinutes < 60) return `${diffMinutes}m ago`;

  const diffHours = Math.round(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours}h ago`;

  return `${Math.round(diffHours / 24)}d ago`;
}

export function latestTimestamp(values: Array<string | undefined | null>): string | null {
  const timestamps = values
    .filter((value): value is string => Boolean(value))
    .map((value) => new Date(value).getTime())
    .filter(Number.isFinite);

  if (timestamps.length === 0) return null;
  return new Date(Math.max(...timestamps)).toISOString();
}

export function isPlatformId(value: string): value is PlatformId {
  return platformCatalog.some((platform) => platform.id === value);
}

export function platformName(platformId: string): string {
  return platformCatalog.find((platform) => platform.id === platformId)?.name ?? platformId;
}

export function platformHoverClass(platformId?: PlatformId | null, fallback = 'hover:border-border-subtle hover:bg-accent-soft hover:text-accent-strong'): string {
  if (platformId === 'youtube') return 'hover:border-red-300 hover:bg-red-50 hover:text-red-700';
  if (platformId === 'spotify') return 'hover:border-emerald-300 hover:bg-emerald-50 hover:text-emerald-700';
  if (platformId === 'apple') return 'hover:border-rose-300 hover:bg-rose-50 hover:text-rose-700';
  if (platformId === 'tidal') return 'hover:border-slate-400 hover:bg-surface-subtle hover:text-content';
  return fallback;
}

export function playlistLastSyncedAt(playlist: MusicLibraryPlaylist): string | null {
  return latestTimestamp(playlist.services.map((service) => service.lastSyncedAt));
}

export function visiblePlatformNames(song: MusicLibrarySong): string[] {
  const names = song.sourcePlatforms
    .filter((source) => source !== 'musicbrainz')
    .map(platformName);

  return names.length > 0 ? names : ['Library'];
}

export function visiblePlatformIds(song: MusicLibrarySong): PlatformId[] {
  return song.sourcePlatforms.filter(isPlatformId);
}

export function songArtwork(song: MusicLibrarySong, index = 0): string {
  return song.thumbnailUrl || fallbackArtwork[index % fallbackArtwork.length];
}

export function playlistArtwork(playlist: MusicLibraryPlaylist, index = 0): string {
  const seed = playlist.services.at(0)?.servicePlaylistId ?? playlist.id;
  const hash = Array.from(seed).reduce((total, char) => total + char.charCodeAt(0), index);
  return fallbackArtwork[hash % fallbackArtwork.length];
}

export function songArtist(song: MusicLibrarySong): string {
  return song.artist || 'Unknown artist';
}
