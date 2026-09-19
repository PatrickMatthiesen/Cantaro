import { mediaFormatLabel } from '../../services/mediaFormatting';

export const libraryCollectionStorageKey = 'cantaro.media.library.collection';

export function libraryFormatLabel(mediaKind: string, format?: string, collection?: string): string | undefined {
  const normalized = format?.toLowerCase();
  if (collection === 'anime' && (!normalized || normalized === 'tv')) return undefined;
  if (collection === 'manga' && (!normalized || normalized === 'manga')) return undefined;
  if (collection === 'books' && (!normalized || normalized === 'novel')) return undefined;
  if (mediaKind === 'anime' && collection === 'film-tv') {
    return !normalized || normalized === 'tv' ? 'Anime' : `Anime · ${mediaFormatLabel(normalized)}`;
  }
  return normalized ? mediaFormatLabel(normalized) : undefined;
}

export function libraryCollection(value: unknown): string | undefined {
  return typeof value === 'string' && ['film-tv', 'anime', 'manga', 'books'].includes(value)
    ? value : undefined;
}

export function collectionForMediaKind(kind?: string): string | undefined {
  if (kind === 'anime') return 'anime';
  if (kind === 'manga' || kind === 'oneShot') return 'manga';
  if (kind === 'lightNovel') return 'books';
  if (kind === 'movie' || kind === 'series') return 'film-tv';
  return undefined;
}
