export type MediaProviderId = 'anilist';

export interface MediaProviderCatalogEntry {
  id: MediaProviderId;
  name: string;
  icon: string;
  gradient: string;
  implemented: boolean;
  description: string;
}

export const mediaProviderCatalog: MediaProviderCatalogEntry[] = [
  {
    id: 'anilist',
    name: 'AniList',
    icon: '🎌',
    gradient: 'from-blue-500 to-cyan-500',
    implemented: true,
    description: 'Anime and manga tracking — import your list and keep progress in sync.',
  },
];
