import { mediaFormatLabel } from '../../services/mediaFormatting';

export const libraryCollectionStorageKey = 'cantaro.media.library.collection';

const defaultCollectionFormats = new Map<string | undefined, string>([
  ['anime', 'tv'],
  ['manga', 'manga'],
  ['books', 'novel'],
]);

const mediaKindCollections = new Map([
  ['anime', 'anime'],
  ['manga', 'manga'],
  ['oneShot', 'manga'],
  ['lightNovel', 'books'],
  ['movie', 'film-tv'],
  ['series', 'film-tv'],
]);

function animeFilmTvLabel(format?: string): string {
  return !format || format === 'tv' ? 'Anime' : `Anime · ${mediaFormatLabel(format)}`;
}

export function libraryFormatLabel(mediaKind: string, format?: string, collection?: string): string | undefined {
  const normalized = format?.toLowerCase();
  if (mediaKind === 'anime' && collection === 'film-tv') {
    return animeFilmTvLabel(normalized);
  }
  if (!normalized || normalized === defaultCollectionFormats.get(collection)) return undefined;
  return mediaFormatLabel(normalized);
}

export function libraryCollection(value: unknown): string | undefined {
  return typeof value === 'string' && ['film-tv', 'anime', 'manga', 'books'].includes(value)
    ? value : undefined;
}

export function collectionForMediaKind(kind?: string): string | undefined {
  return mediaKindCollections.get(kind ?? '');
}
