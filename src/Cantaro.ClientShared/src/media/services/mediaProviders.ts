export type MediaProviderId = 'anilist' | 'myanimelist';

export interface MediaProviderCatalogEntry {
  id: MediaProviderId;
  name: string;
  iconId: MediaProviderId;
  gradient: string;
  implemented: boolean;
  description: string;
}

export const mediaProviderCatalog: MediaProviderCatalogEntry[] = [
  {
    id: 'anilist',
    name: 'AniList',
    iconId: 'anilist',
    gradient: 'from-blue-500 to-cyan-500',
    implemented: true,
    description: 'Anime and manga tracking — import your list and keep progress in sync.',
  },
  {
    id: 'myanimelist',
    name: 'MyAnimeList',
    iconId: 'myanimelist',
    gradient: 'from-blue-700 to-blue-500',
    implemented: true,
    description: 'Anime and manga tracking — import your MAL lists and sync progress and scores.',
  },
];

export const mainMediaProviderId = mediaProviderCatalog[0]?.id ?? 'anilist';
