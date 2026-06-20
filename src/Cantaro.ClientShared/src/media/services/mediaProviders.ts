export type MediaProviderId = 'anilist';

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
];

export const mainMediaProviderId = mediaProviderCatalog[0]?.id ?? 'anilist';
