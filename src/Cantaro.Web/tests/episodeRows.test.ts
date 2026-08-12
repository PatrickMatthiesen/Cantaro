import { describe, expect, it } from 'bun:test';
import type { MediaEntryDetailModel } from '../../Cantaro.ClientShared/src/media/services/mediaApi';
import type { EpisodeStreamingDestinations } from '../../Cantaro.ClientShared/src/media/services/streamingDestinations';
import { getEpisodeRows } from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/episodeRows';

function createEntry(episodeCount: number | null | undefined, progressEpisodes: number) {
  return {
    progressEpisodes,
    title: { episodeCount },
  } as MediaEntryDetailModel;
}

function createEpisodes(count: number) {
  return Array.from({ length: count }, (_, index) => ({
    episodeNumber: index + 1,
    destinations: [],
  })) as EpisodeStreamingDestinations[];
}

describe('getEpisodeRows', () => {
  it('does not invent episode one when the episode count and catalog are unknown', () => {
    const rows = getEpisodeRows(createEntry(null, 0), []);

    expect(rows).toEqual([]);
  });

  it('still shows observed episodes when the total episode count is unknown', () => {
    const rows = getEpisodeRows(createEntry(null, 0), createEpisodes(2));

    expect(rows.map(row => row.episodeNumber)).toEqual([1, 2]);
  });

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
