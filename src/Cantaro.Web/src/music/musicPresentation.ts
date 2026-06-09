import { platformCatalog, type MusicLibraryPlaylist, type MusicLibrarySong } from '@cantaro/client-shared/music';

export const fallbackArtwork = [
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

export function platformName(platformId: string): string {
  return platformCatalog.find((platform) => platform.id === platformId)?.name ?? platformId;
}

export function visiblePlatformNames(song: MusicLibrarySong): string[] {
  const names = song.sourcePlatforms
    .filter((source) => source !== 'musicbrainz')
    .map(platformName);

  return names.length > 0 ? names : ['Library'];
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
