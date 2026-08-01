import { describe, expect, test } from 'bun:test';
import { progressSegments } from '../../Cantaro.ClientShared/src/media/components/LibraryEntryCard';
import type { MediaLibraryListItemDto } from '../../Cantaro.ClientShared/src/media/services/mediaApi.types';

function makeEntry(overrides: Partial<MediaLibraryListItemDto> = {}): MediaLibraryListItemDto {
  return {
    id: 'entry-1',
    mediaTitleId: 'title-1',
    canonicalTitle: 'Test series',
    mediaKind: 'anime',
    normalizedStatus: 'current',
    progressEpisodes: 12,
    episodeCount: 24,
    releasedCount: 15,
    primaryProgressDimension: 'episode',
    provider: 'anilist',
    providerMediaId: '1',
    isConnected: true,
    updatedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

describe('progressSegments', () => {
  test('separates watched, released-unwatched, and future episodes', () => {
    expect(progressSegments(makeEntry())).toEqual({
      watched: 12,
      releasedUnwatched: 3,
      remaining: 9,
      total: 24,
      visualTotal: 24,
    });
  });

  test('clamps stale release metadata behind watched progress', () => {
    expect(progressSegments(makeEntry({ progressEpisodes: 8, releasedCount: 5 }))).toEqual({
      watched: 8,
      releasedUnwatched: 0,
      remaining: 16,
      total: 24,
      visualTotal: 24,
    });
  });

  test.each([
    { releasedCount: 8, visualTotal: 12 },
    { releasedCount: 12, visualTotal: 13 },
    { releasedCount: 13, visualTotal: 24 },
    { releasedCount: 23, visualTotal: 24 },
    { releasedCount: 24, visualTotal: 24 / 0.9 },
    { releasedCount: 100, visualTotal: 100 / 0.9 },
  ])('uses an open-ended visual scale for $releasedCount released episodes', ({ releasedCount, visualTotal }) => {
    expect(progressSegments(makeEntry({
      progressEpisodes: Math.min(6, releasedCount),
      episodeCount: undefined,
      releasedCount,
    }))).toEqual({
      watched: Math.min(6, releasedCount),
      releasedUnwatched: releasedCount - Math.min(6, releasedCount),
      remaining: null,
      total: null,
      visualTotal,
    });
  });

  test('hides an unknown-total bar when no episode progress is known', () => {
    expect(progressSegments(makeEntry({
      progressEpisodes: 0,
      episodeCount: undefined,
      releasedCount: undefined,
    }))).toBeNull();
  });

  test('keeps non-episode unknown progress open-ended instead of applying anime cour sizes', () => {
    expect(progressSegments(makeEntry({
      primaryProgressDimension: 'chapter',
      progressChapters: 10,
      episodeCount: undefined,
      chapterCount: undefined,
      releasedCount: 20,
    }))).toEqual({
      watched: 10,
      releasedUnwatched: 0,
      remaining: null,
      total: null,
      visualTotal: 10 / 0.9,
    });
  });
});
