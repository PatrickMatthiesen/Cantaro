import { describe, expect, it } from 'bun:test';
import type { MediaLibraryEntryDetailDto } from '../../Cantaro.ClientShared/src/media/services/mediaApi';
import type { EpisodeStreamingDestinations } from '../../Cantaro.ClientShared/src/media/services/streamingDestinations';
import { getEpisodeRows } from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/episodeRows';

function createEntry(episodeCount: number, progressEpisodes: number) {
  return {
    progressEpisodes,
    title: { episodeCount },
  } as MediaLibraryEntryDetailDto;
}

function createEpisodes(count: number) {
  return Array.from({ length: count }, (_, index) => ({
    episodeNumber: index + 1,
    destinations: [],
  })) as EpisodeStreamingDestinations[];
}

describe('getEpisodeRows', () => {
  it('does not add an episode beyond the known episode count', () => {
    const rows = getEpisodeRows(createEntry(170, 170), createEpisodes(170));

    expect(rows).toHaveLength(170);
    expect(rows.at(-1)?.episodeNumber).toBe(170);
    expect(rows.some((row) => row.episodeNumber === 171)).toBeFalse();
  });

  it('includes the next episode while progress remains below the known count', () => {
    const rows = getEpisodeRows(createEntry(170, 169), createEpisodes(169));

    expect(rows.at(-1)?.episodeNumber).toBe(170);
  });
});
