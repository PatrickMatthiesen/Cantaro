export type MediaProviderId = 'anilist' | 'myanimelist' | 'simkl';

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
  {
    id: 'simkl',
    name: 'SIMKL',
    iconId: 'simkl',
    gradient: 'from-orange-500 to-red-500',
    implemented: true,
    description: 'Import your SIMKL movie, TV, and anime watch history and keep progress in sync.',
  },
];

export const mainMediaProviderId = mediaProviderCatalog[0]?.id ?? 'anilist';

export function mediaProviderIconId(providerId?: string): MediaProviderId | undefined {
  if (!providerId) {
    return undefined;
  }

  const normalizedProviderId = providerId.trim().toLowerCase();
  return mediaProviderCatalog.some((provider) => provider.iconId === normalizedProviderId)
    ? normalizedProviderId as MediaProviderId
    : undefined;
}

export function connectedMediaProviderIds(
  statuses: ReadonlyArray<{ providerId: string; isConnected: boolean }>,
): string[] {
  return statuses.filter((status) => status.isConnected).map((status) => status.providerId);
}
