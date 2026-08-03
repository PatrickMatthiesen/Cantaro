import { parseHTML } from 'linkedom';
import { describe, expect, it } from 'vitest';
import {
  buildCrunchyrollSeriesObservation,
  seriesObservationFingerprint,
} from '../crunchyrollSeriesCatalog';

describe('Crunchyroll series catalog extraction', () => {
  it('collects one safe destination per rendered episode card', () => {
    const { document } = parseHTML(`
      <html>
        <body>
          <h1>That Time I Got Reincarnated as a Slime</h1>
          <button aria-haspopup="listbox">Season 4</button>
          <section data-t="episode-card ">
            <a href="/watch/GE00374365ENUS/new-days">23m</a>
            <h3><a href="/watch/GE00374365ENUS/new-days">E1 - New Days</a></h3>
          </section>
          <section data-t="episode-card ">
            <a href="/watch/GE00374366ENUS/the-dungeon-evolves" aria-label="Play Episode 2 - The Dungeon Evolves"></a>
            <h3>E2 - The Dungeon Evolves</h3>
          </section>
          <a href="/watch/SHOULDNOTBEINCLUDED/other-series">E99 - Other</a>
        </body>
      </html>
    `);

    const observation = buildCrunchyrollSeriesObservation(
      document,
      'https://www.crunchyroll.com/series/GYZJ43JMR/that-time-i-got-reincarnated-as-a-slime',
    );

    expect(observation).toMatchObject({
      siteMediaId: 'GYZJ43JMR:season-4',
      titleText: 'That Time I Got Reincarnated as a Slime Season 4',
      seriesTitle: 'That Time I Got Reincarnated as a Slime Season 4',
      seasonTitle: 'Season 4',
      seasonNumber: 4,
      providerSeriesId: 'GYZJ43JMR',
      progressHint: null,
      observedEpisodes: [
        {
          providerEpisodeId: 'GE00374365ENUS',
          providerUrl: 'https://www.crunchyroll.com/watch/GE00374365ENUS/new-days',
          episodeNumber: 1,
          episodeTitle: 'New Days',
        },
        {
          providerEpisodeId: 'GE00374366ENUS',
          providerUrl: 'https://www.crunchyroll.com/watch/GE00374366ENUS/the-dungeon-evolves',
          episodeNumber: 2,
          episodeTitle: 'The Dungeon Evolves',
        },
      ],
    });
  });

  it('uses the selected season option when its menu is rendered', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1>
      <div role="option" aria-selected="true"><span>OVA Season 1</span><span>5 Episodes</span></div>
      <article data-t="episode-card ">
        <h3>E1 - A Special</h3>
        <a href="/watch/OVAEP1/a-special"></a>
      </article>
    `);

    const observation = buildCrunchyrollSeriesObservation(
      document,
      'https://www.crunchyroll.com/series/SERIES1/my-show',
    );

    expect(observation?.seasonTitle).toBe('OVA Season 1');
    expect(observation?.siteMediaId).toBe('SERIES1:ova-season-1');
  });

  it('collects labelled episode links and reads Crunchyroll season info when old card markers are absent', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1>
      <div class="season-info"><span>Season 4</span><span>16 Episodes</span></div>
      <a href="/watch/EP1/one" aria-label="Play Episode 1 - One"></a>
      <a href="/watch/RELATED99/other-series">E99 - Other</a>
    `);

    const observation = buildCrunchyrollSeriesObservation(
      document,
      'https://www.crunchyroll.com/series/SERIES1/my-show',
    );

    expect(observation).toMatchObject({
      siteMediaId: 'SERIES1:season-4',
      titleText: 'My Show Season 4',
      seasonTitle: 'Season 4',
      seasonNumber: 4,
      providerSeriesId: 'SERIES1',
      observedEpisodes: [{
        providerEpisodeId: 'EP1',
        episodeNumber: 1,
        episodeTitle: 'One',
      }],
    });
  });

  it('rejects non-Crunchyroll series pages and unsafe episode links', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1>
      <button>Season 1</button>
      <article data-t="episode-card ">
        <h3>E1 - Redirect</h3>
        <a href="https://crunchyrollx.com/watch/EVIL1/redirect"></a>
      </article>
    `);

    expect(buildCrunchyrollSeriesObservation(
      document,
      'https://www.crunchyroll.com/series/SERIES1/my-show',
    )).toBeNull();
    expect(buildCrunchyrollSeriesObservation(
      document,
      'https://crunchyrollx.com/series/SERIES1/my-show',
    )).toBeNull();
  });

  it('fingerprints the season and rendered provider IDs', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1>
      <button>Season 1</button>
      <article data-t="episode-card "><h3>E1 - One</h3><a href="/watch/EP1/one"></a></article>
    `);
    const observation = buildCrunchyrollSeriesObservation(
      document,
      'https://www.crunchyroll.com/series/SERIES1/my-show',
    );

    expect(observation && seriesObservationFingerprint(observation)).toBe('SERIES1:season-1|EP1');
  });
});
