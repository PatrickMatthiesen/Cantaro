import { describe, expect, it } from 'bun:test';
import {
  formatEpisodeNumber,
  getDefaultSeasonSelection,
  getSeasonOptions,
  getSelectedSeasonProgress,
  mapSeasonProgressToOverall,
} from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/seasonEpisodes';
import type { EpisodeStreamingDestinations } from '../../Cantaro.ClientShared/src/media/services/streamingDestinations';

function episode(
  episodeNumber: number,
  seasonNumber?: number,
  seasonEpisodeNumber?: number,
): EpisodeStreamingDestinations {
  return {
    episodeNumber,
    seasonNumber,
    seasonEpisodeNumber,
    availableAudioLanguageCodes: [],
    availableSubtitleLanguageCodes: [],
    destinations: [],
  };
}

const twoSeasons = [
  episode(1, 1, 1),
  episode(2, 1, 2),
  episode(3, 2, 1),
  episode(4, 2, 2),
];

const shadowhuntersSeasons = [
  ...Array.from({ length: 13 }, (_, index) => episode(index + 1, 1, index + 1)),
  ...Array.from({ length: 20 }, (_, index) => episode(index + 14, 2, index + 1)),
  ...Array.from({ length: 22 }, (_, index) => episode(index + 34, 3, index + 1)),
];

describe('season episode mapping', () => {
  it('builds regular season choices and keeps specials separate', () => {
    expect(getSeasonOptions(twoSeasons, 2)).toEqual([
      { value: 'all', label: 'All seasons', episodeCount: 4 },
      { value: 1, label: 'Season 1', episodeCount: 2 },
      { value: 2, label: 'Season 2', episodeCount: 2 },
      { value: 'specials', label: 'Specials', episodeCount: 2 },
    ]);
  });

  it('defaults to the season containing the next unwatched episode at a boundary', () => {
    expect(getDefaultSeasonSelection(twoSeasons, 0)).toBe(1);
    expect(getDefaultSeasonSelection(twoSeasons, 2)).toBe(2);
    expect(getDefaultSeasonSelection(twoSeasons, 4)).toBe(2);
  });

  it('shows Shadowhunters overall episode 43 as season 3 episode 10', () => {
    expect(getDefaultSeasonSelection(shadowhuntersSeasons, 43)).toBe(3);
    expect(getSelectedSeasonProgress(shadowhuntersSeasons, 3, 43)).toEqual({
      seasonNumber: 3,
      value: 10,
      total: 22,
      overallValue: 43,
      canEdit: true,
    });
    expect(mapSeasonProgressToOverall(shadowhuntersSeasons, 3, 10)).toBe(43);
  });

  it('maps relative edits back to the canonical overall prefix', () => {
    expect(getSelectedSeasonProgress(twoSeasons, 2, 3)).toEqual({
      seasonNumber: 2,
      value: 1,
      total: 2,
      overallValue: 3,
      canEdit: true,
    });
    expect(mapSeasonProgressToOverall(twoSeasons, 2, 0)).toBe(2);
    expect(mapSeasonProgressToOverall(twoSeasons, 2, 2)).toBe(4);
  });

  it('keeps partial trusted labels but disables unsafe progress mapping', () => {
    const partial = [episode(12, 2, 2), episode(13, 2, 3)];

    expect(formatEpisodeNumber(partial[0]!, 2)).toEqual({
      primary: 'Episode 2',
      secondary: 'Overall episode 12',
    });
    expect(getSelectedSeasonProgress(partial, 2, 12)?.canEdit).toBeFalse();
    expect(mapSeasonProgressToOverall(partial, 2, 2)).toBeNull();
  });

  it('rejects progress edits when a season has an internal mapping gap', () => {
    const gap = [episode(11, 2, 1), episode(13, 2, 3)];

    expect(getSelectedSeasonProgress(gap, 2, 11)?.canEdit).toBeFalse();
    expect(mapSeasonProgressToOverall(gap, 2, 1)).toBeNull();
  });

  it('falls back to overall numbering when season metadata is absent', () => {
    const legacyEpisode = episode(7);

    expect(getSeasonOptions([legacyEpisode])).toEqual([
      { value: 'all', label: 'All seasons', episodeCount: 1 },
    ]);
    expect(getDefaultSeasonSelection([legacyEpisode], 6)).toBe('all');
    expect(formatEpisodeNumber(legacyEpisode, 'all')).toEqual({
      primary: 'Episode 7',
    });
  });
});
