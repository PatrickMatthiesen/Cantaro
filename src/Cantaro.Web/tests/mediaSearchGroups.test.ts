import { describe, expect, test } from 'bun:test';
import { mediaSearchCategory } from '../src/search/mediaSearchGroups';
import type { SearchResultItem } from '../src/search/searchApi';

function result(mediaKind?: string): SearchResultItem {
  return { entityType: 'media', id: '1', title: 'Suzume', canonicalRoute: '/media/1', mediaKind };
}

describe('media search categories', () => {
  test('separates watchable adaptations from written titles', () => {
    for (const kind of ['anime', 'movie', 'series']) {
      expect(mediaSearchCategory(result(kind))).toBe('watch');
    }
    expect(mediaSearchCategory(result('manga'))).toBe('read');
  });

  test('keeps missing and unknown kinds visible without guessing from the title', () => {
    for (const kind of [undefined, 'other', 'future-format']) {
      expect(mediaSearchCategory(result(kind))).toBe('other');
    }
  });
});
