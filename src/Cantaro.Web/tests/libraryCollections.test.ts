import { describe, expect, test } from 'bun:test';
import { createInitialMediaLibraryFilters } from '../../Cantaro.ClientShared/src/media/components/media-library/mediaLibraryFilters';
import { libraryFormatLabel } from '../../Cantaro.ClientShared/src/media/components/media-library/libraryCollections';
import { getMediaFilterDefaults, mediaLibraryFilterSearch } from '../src/media/mediaLibraryRouteFilters';

describe('library collections', () => {
  test('an explicit collection or old category link overrides the remembered preference', () => {
    const original = Object.getOwnPropertyDescriptor(globalThis, 'window');
    Object.defineProperty(globalThis, 'window', { configurable: true, value: { localStorage: { getItem: () => 'books' } } });
    try {
      expect(createInitialMediaLibraryFilters().collection).toBe('books');
      expect(createInitialMediaLibraryFilters({ collection: 'anime' }).collection).toBe('anime');
      expect(createInitialMediaLibraryFilters({ mediaKind: 'manga' }).collection).toBe('manga');
    } finally {
      if (original) Object.defineProperty(globalThis, 'window', original);
      else Reflect.deleteProperty(globalThis, 'window');
    }
  });
  test('preserves collection and format through a URL round trip', () => {
    const filters = { collection: 'anime', format: 'movie', status: '', provider: '', page: 2 };
    expect(createInitialMediaLibraryFilters(getMediaFilterDefaults(mediaLibraryFilterSearch(filters))))
      .toMatchObject(filters);
  });

  test('old anime and manga links select the corresponding collection', () => {
    expect(createInitialMediaLibraryFilters({ mediaKind: 'anime' }).collection).toBe('anime');
    expect(createInitialMediaLibraryFilters({ mediaKind: 'manga' }).collection).toBe('manga');
  });

  test('old written-format links use the stored format instead of a nonexistent category', () => {
    expect(getMediaFilterDefaults({ mediaKind: 'lightNovel' })).toMatchObject({ collection: 'books', format: 'novel' });
    expect(getMediaFilterDefaults({ mediaKind: 'oneShot' })).toMatchObject({ collection: 'manga', format: 'one_shot' });
    expect(getMediaFilterDefaults({ mediaKind: 'lightNovel' })).not.toHaveProperty('mediaKind');
  });

  test('uses contextual labels without duplicating categories', () => {
    expect(libraryFormatLabel('anime', 'TV', 'anime')).toBeUndefined();
    expect(libraryFormatLabel('anime', 'MOVIE', 'anime')).toBe('Movie');
    expect(libraryFormatLabel('anime', 'OVA', 'anime')).toBe('OVA');
    expect(libraryFormatLabel('anime', 'MOVIE', 'film-tv')).toBe('Anime · Movie');
    expect(libraryFormatLabel('movie', 'movie', 'film-tv')).toBe('Movie');
    expect(libraryFormatLabel('manga', 'MANGA', 'manga')).toBeUndefined();
  });
});
