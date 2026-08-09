import { describe, expect, it } from 'vitest';
import type { SeriesCatalogObservation } from '../../../../contracts/catalogObservation';
import { buildSeriesDiscoveryLog } from './seriesLogging';

describe('Crunchyroll series discovery logging', () => {
  it('identifies the season and every extracted episode without unrelated observation fields', () => {
    const observation: SeriesCatalogObservation = {
      schemaVersion: 1,
      provider: 'crunchyroll',
      providerSeriesId: 'SERIES1',
      providerSeasonId: 'SEASON4',
      seriesTitle: 'My Show',
      seasonTitle: 'Season 4',
      seasonNumber: 4,
      seriesUrl: 'https://www.crunchyroll.com/series/SERIES1/my-show',
      observedAt: '2026-08-08T12:00:00.000Z',
      extensionVersion: '0.1.0',
      episodes: [
        {
          episodeNumber: 1,
          episodeTitle: 'The Beginning',
          providerEpisodeId: 'EPISODE1',
          providerUrl: 'https://www.crunchyroll.com/watch/EPISODE1/the-beginning',
        },
        {
          episodeNumber: 2,
          providerEpisodeId: 'EPISODE2',
          providerUrl: 'https://www.crunchyroll.com/watch/EPISODE2/episode-2',
        },
      ],
    };

    expect(buildSeriesDiscoveryLog(observation)).toEqual({
      providerSeriesId: 'SERIES1',
      providerSeasonId: 'SEASON4',
      season: 'Season 4',
      episodeCount: 2,
      episodes: [
        {
          episodeNumber: 1,
          title: 'The Beginning',
          providerEpisodeId: 'EPISODE1',
          url: 'https://www.crunchyroll.com/watch/EPISODE1/the-beginning',
        },
        {
          episodeNumber: 2,
          title: undefined,
          providerEpisodeId: 'EPISODE2',
          url: 'https://www.crunchyroll.com/watch/EPISODE2/episode-2',
        },
      ],
    });
  });
});
