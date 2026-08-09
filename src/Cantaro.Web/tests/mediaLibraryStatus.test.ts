import { describe, expect, test } from 'bun:test';
import { createInitialMediaLibraryFilters } from '../../Cantaro.ClientShared/src/media/components/media-library/mediaLibraryFilters';
import { mediaLibraryStatusLabel } from '../../Cantaro.ClientShared/src/media/components/media-library/mediaLibraryStatus';
import type {
  MediaLibraryEntryDetailDto,
  MediaLibraryListItemDto,
  MediaLibraryPageDto,
} from '../../Cantaro.ClientShared/src/media/services/mediaApi.types';
import { getMediaFilterDefaults } from '../src/media/mediaLibraryRouteFilters';

test('media library contracts preserve plural provider-list memberships', () => {
  const pageLists: MediaLibraryPageDto['availableProviderListNames'] = ['Favorites'];
  const itemLists: MediaLibraryListItemDto['providerListNames'] = ['Favorites', 'Seasonal'];
  const detailLists: MediaLibraryEntryDetailDto['providerListNames'] = itemLists;

  expect({ pageLists, itemLists, detailLists }).toEqual({
    pageLists: ['Favorites'],
    itemLists: ['Favorites', 'Seasonal'],
    detailLists: ['Favorites', 'Seasonal'],
  });
});

describe('media library lifecycle filters', () => {
  test('defaults the library to the canonical current status without a provider list', () => {
    const filters = createInitialMediaLibraryFilters();

    expect(filters).toMatchObject({
      provider: 'anilist',
      status: 'current',
      page: 1,
    });
    expect(filters).not.toHaveProperty('providerListName');
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

  test('preserves explicitly requested canonical status and provider list filters', () => {
    const routeDefaults = getMediaFilterDefaults({
      status: 'completed',
      providerListName: 'Favorites',
    });

    expect(createInitialMediaLibraryFilters(routeDefaults)).toMatchObject({
      status: 'completed',
      providerListName: 'Favorites',
    });
  });
});

describe('media library status labels', () => {
  test('presents repeating as rewatching or rereading based on media kind', () => {
    expect(mediaLibraryStatusLabel('repeating', 'anime')).toBe('Rewatching');
    expect(mediaLibraryStatusLabel('repeating', 'manga')).toBe('Rereading');
  });
});
