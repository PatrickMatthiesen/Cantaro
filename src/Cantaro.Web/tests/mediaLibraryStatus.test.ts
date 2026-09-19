import { describe, expect, test } from 'bun:test';
import { createInitialMediaLibraryFilters } from '../../Cantaro.ClientShared/src/media/components/media-library/mediaLibraryFilters';
import { mediaLibraryStatusLabel } from '../../Cantaro.ClientShared/src/media/components/media-library/mediaLibraryStatus';
import type {
  MediaLibraryListItemDto,
  MediaLibraryPageDto,
  MediaViewerProviderBindingDto,
} from '../../Cantaro.ClientShared/src/media/services/mediaApi.types';
import { getMediaFilterDefaults, mediaLibraryFilterSearch } from '../src/media/mediaLibraryRouteFilters';

test('media library contracts preserve plural provider-list memberships', () => {
  const pageLists: MediaLibraryPageDto['availableProviderListNames'] = ['Favorites'];
  const itemLists: MediaLibraryListItemDto['providerListNames'] = ['Favorites', 'Seasonal'];
  const bindingLists: MediaViewerProviderBindingDto['providerListNames'] = itemLists;

  expect({ pageLists, itemLists, bindingLists }).toEqual({
    pageLists: ['Favorites'],
    itemLists: ['Favorites', 'Seasonal'],
    bindingLists: ['Favorites', 'Seasonal'],
  });
});

describe('media library lifecycle filters', () => {
  test('round trips cleared filters, search, sorting, and pagination through the URL', () => {
    const saved = mediaLibraryFilterSearch({ query: 'azure sea', sortBy: 'title', sortDir: 'asc', page: 3 });
    const restored = createInitialMediaLibraryFilters(getMediaFilterDefaults(saved));
    expect(restored).toMatchObject({ query: 'azure sea', status: '', mediaKind: '', provider: '', sortBy: 'title', sortDir: 'asc', page: 3 });
  });

  test('reads query-only URLs and rejects invalid pages', () => {
    expect(getMediaFilterDefaults({ q: 'slime', page: '-2' })).toEqual({ query: 'slime', page: 1 });
    expect(getMediaFilterDefaults({ page: '2' })?.page).toBe(2);
  });
  test('defaults the library to the canonical current status without a provider list', () => {
    const filters = createInitialMediaLibraryFilters();

    expect(filters).toMatchObject({
      collection: 'film-tv',
      status: 'current',
      page: 1,
    });
    expect(filters).not.toHaveProperty('providerListName');
    expect(filters.provider).toBeUndefined();
  });

  test('keeps the current default when route defaults only specify another filter', () => {
    const routeDefaults = getMediaFilterDefaults({ sortBy: 'title' });

    expect(routeDefaults).toEqual({ page: 1, sortBy: 'title' });
    expect(createInitialMediaLibraryFilters(routeDefaults).status).toBe('current');
  });

  test('does not reinstate the legacy stored Watching provider list', () => {
    const originalDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
    let storageWasRead = false;
    Object.defineProperty(globalThis, 'localStorage', {
      configurable: true,
      value: {
        getItem: () => {
          storageWasRead = true;
          return 'Watching';
        },
      } as unknown as Storage,
    });

    try {
      const filters = createInitialMediaLibraryFilters();
      expect(storageWasRead).toBeFalse();
      expect(filters).not.toHaveProperty('providerListName');
    } finally {
      if (originalDescriptor) {
        Object.defineProperty(globalThis, 'localStorage', originalDescriptor);
      } else {
        Reflect.deleteProperty(globalThis, 'localStorage');
      }
    }
  });

  test('preserves canonical status while ignoring provider-list route filters', () => {
    const routeDefaults = getMediaFilterDefaults({
      status: 'completed',
      providerListName: 'Favorites',
    });

    const filters = createInitialMediaLibraryFilters(routeDefaults);
    expect(filters.status).toBe('completed');
    expect(filters).not.toHaveProperty('providerListName');
  });
});

describe('media library status labels', () => {
  test('presents repeating as rewatching or rereading based on media kind', () => {
    expect(mediaLibraryStatusLabel('repeating', 'anime')).toBe('Rewatching');
    expect(mediaLibraryStatusLabel('repeating', 'manga')).toBe('Rereading');
  });
});
