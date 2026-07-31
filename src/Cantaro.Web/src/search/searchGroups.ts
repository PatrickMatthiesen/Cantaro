import type { SearchResultGroupId } from './searchApi';
import type { SearchGroupId } from './searchState';

export interface SearchGroupPresentation {
  id: SearchResultGroupId;
  label: string;
  singular: string;
  description: string;
}

export const searchGroups: SearchGroupPresentation[] = [
  { id: 'songs', label: 'Songs', singular: 'song', description: 'Recordings in your music archive' },
  { id: 'artists', label: 'Artists', singular: 'artist', description: 'Artists connected to your saved music' },
  { id: 'playlists', label: 'Playlists', singular: 'playlist', description: 'Collections owned by you' },
  { id: 'media', label: 'Media', singular: 'title', description: 'Movies, series, anime, and manga' },
];

export const searchTabs: Array<{ id: SearchGroupId; label: string }> = [
  { id: 'all', label: 'All' },
  ...searchGroups.map(({ id, label }) => ({ id, label })),
];
