import { parseHTML } from 'linkedom';
import { describe, expect, it } from 'vitest';
import { MAX_CATALOG_EPISODES_PER_OBSERVATION } from '../../../../contracts/catalogObservation';
import { catalogObservationFingerprint, inspectSeriesPage } from './seriesParser';

const SERIES_URL = 'https://www.crunchyroll.com/series/GYZJ43JMR/that-time-i-got-reincarnated-as-a-slime';

describe('Crunchyroll series catalog extraction', () => {
  it('collects one safe destination per rendered episode card', () => {
    const { document } = parseHTML(`
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
    `);

    expect(inspectSeriesPage(document, SERIES_URL).observation).toMatchObject({
      provider: 'crunchyroll',
      providerSeriesId: 'GYZJ43JMR',
      seriesTitle: 'That Time I Got Reincarnated as a Slime',
      seasonTitle: 'Season 4',
      seasonNumber: 4,
      episodes: [
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
      <article data-t="episode-card "><h3>E1 - A Special</h3><a href="/watch/OVAEP1/a-special"></a></article>
    `);

    expect(inspectSeriesPage(document, 'https://www.crunchyroll.com/series/SERIES1/my-show').observation)
      .toMatchObject({ seasonTitle: 'OVA Season 1', seasonNumber: 1 });
  });

  it('preserves a split-season label while extracting its base season number', () => {
    const { document } = parseHTML(`
      <h1>Black Clover</h1>
      <div class="season-info"><span>  SEASON  1   PART  3 </span><span>17 Episodes</span></div>
      <article data-t="episode-card "><h3>E1 - The New Beginning</h3><a href="/watch/BLACKCLOVER1/new-beginning"></a></article>
    `);

    expect(inspectSeriesPage(
      document,
      'https://www.crunchyroll.com/series/GRE50KV36/black-clover',
    ).observation).toMatchObject({
      seasonTitle: 'SEASON 1 PART 3',
      seasonNumber: 1,
      episodes: [{ providerEpisodeId: 'BLACKCLOVER1', episodeNumber: 1 }],
    });
  });

  it('reads a split-season title and identity from Crunchyroll attributes', () => {
    const { document } = parseHTML(`
      <h1>Black Clover</h1>
      <h4 currentseasonid="BLACKCLOVER-PART2" seasondisplaynumber="1" seasontitle="Season 1 Part 2">
        Season 1 Part 2
      </h4>
      <article data-t="episode-card "><h3>E52 - Whoever's Strongest Wins</h3><a href="/watch/BLACKCLOVER52/strongest-wins"></a></article>
    `);

    expect(inspectSeriesPage(
      document,
      'https://www.crunchyroll.com/series/GRE50KV36/black-clover',
    ).observation).toMatchObject({
      providerSeasonId: 'BLACKCLOVER-PART2',
      seasonTitle: 'Season 1 Part 2',
      seasonNumber: 1,
      episodes: [{ providerEpisodeId: 'BLACKCLOVER52', episodeNumber: 52 }],
    });
  });

  it('uses labelled links and season-info when card markers are absent', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1>
      <div class="season-info"><span>Season 4</span><span>16 Episodes</span></div>
      <a href="/watch/EP1/one" aria-label="Play Episode 1 - One"></a>
      <a href="/watch/RELATED99/other-series">E99 - Other</a>
    `);

    expect(inspectSeriesPage(document, 'https://www.crunchyroll.com/series/SERIES1/my-show').observation)
      .toMatchObject({
        providerSeriesId: 'SERIES1',
        seriesTitle: 'My Show',
        seasonTitle: 'Season 4',
        seasonNumber: 4,
        episodes: [{ providerEpisodeId: 'EP1', episodeNumber: 1, episodeTitle: 'One' }],
      });
  });

  it('collects a long-running Crunchyroll season beyond the old fixed limit', () => {
    const cards = Array.from({ length: 293 }, (_, index) => {
      const episodeNumber = index + 1;
      return `<article data-t="episode-card "><h3>E${episodeNumber} - Episode ${episodeNumber}</h3><a href="/watch/BORUTO${episodeNumber}/episode-${episodeNumber}"></a></article>`;
    }).join('');
    const { document } = parseHTML(`
      <h1>BORUTO: NARUTO NEXT GENERATIONS</h1>
      <button>Season 1</button>
      ${cards}
    `);

    const observation = inspectSeriesPage(
      document,
      'https://www.crunchyroll.com/series/GR75Q020Y/boruto-naruto-next-generations',
    ).observation;

    expect(observation?.episodes).toHaveLength(293);
    expect(observation?.episodes.at(-1)).toMatchObject({
      providerEpisodeId: 'BORUTO293',
      episodeNumber: 293,
    });
  });

  it('keeps the defensive catalog-observation ceiling without treating it as a season limit', () => {
    const cards = Array.from(
      { length: MAX_CATALOG_EPISODES_PER_OBSERVATION + 1 },
      (_, index) => {
        const episodeNumber = index + 1;
        return `<article data-t="episode-card "><h3>E${episodeNumber}</h3><a href="/watch/LONG${episodeNumber}/episode-${episodeNumber}"></a></article>`;
      },
    ).join('');
    const { document } = parseHTML(`<h1>Long Show</h1><button>Season 1</button>${cards}`);

    const observation = inspectSeriesPage(
      document,
      'https://www.crunchyroll.com/series/LONGSERIES/long-show',
    ).observation;

    expect(observation?.episodes).toHaveLength(MAX_CATALOG_EPISODES_PER_OBSERVATION);
    expect(observation?.episodes.at(-1)?.episodeNumber)
      .toBe(MAX_CATALOG_EPISODES_PER_OBSERVATION);
  });

  it('uses Crunchyroll season attributes when a dubbed season has no numbered label', () => {
    const { document } = parseHTML(`
      <h1>FAIRY TAIL 100 YEARS QUEST</h1>
      <div class="seasons-select">
        <h4 currentseasonid="G649C7NJ9" seasondisplaynumber=""
            seasontitle="FAIRY TAIL 100 YEARS QUEST (English Dub)">
          FAIRY TAIL 100 YEARS QUEST (English Dub)
        </h4>
      </div>
      <article data-t="episode-card ">
        <h3>E1 - The First Guild</h3>
        <a href="/watch/GVWU0M4XG/the-first-guild"></a>
      </article>
      <article data-t="episode-card ">
        <h3>E13.5 - Lucy's Diary</h3>
        <a href="/watch/GWDU7VP0E/going-off-topic-lucys-diary"></a>
      </article>
    `);

    expect(inspectSeriesPage(
      document,
      'https://www.crunchyroll.com/series/GG5H5XQED/fairy-tail-100-years-quest',
    ).observation).toMatchObject({
      providerSeasonId: 'G649C7NJ9',
      seasonTitle: 'FAIRY TAIL 100 YEARS QUEST (English Dub)',
      seasonNumber: undefined,
      episodes: [{
        providerEpisodeId: 'GVWU0M4XG',
        episodeNumber: 1,
        availableAudioLanguageCodes: ['en'],
      }],
    });
  });

  it('rejects non-Crunchyroll pages and unsafe episode links', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1><button>Season 1</button>
      <article data-t="episode-card "><h3>E1 - Redirect</h3><a href="https://crunchyrollx.com/watch/EVIL1/redirect"></a></article>
    `);

    expect(inspectSeriesPage(document, 'https://www.crunchyroll.com/series/SERIES1/my-show').observation)
      .toBeNull();
    expect(inspectSeriesPage(document, 'https://crunchyrollx.com/series/SERIES1/my-show').observation)
      .toBeNull();
  });

  it('explains when rendered cards cannot produce safe destinations', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1>
      <div class="season-info"><span>Season 1</span><span>12 Episodes</span></div>
      <article data-t="episode-card "><h3>E1 - Missing link</h3></article>
      <a href="https://crunchyrollx.com/watch/UNSAFE/redirect" aria-label="Play Episode 2 - Redirect"></a>
    `);

    expect(inspectSeriesPage(document, 'https://www.crunchyroll.com/series/SERIES1/my-show').diagnostics)
      .toEqual({
        issue: 'no_rendered_episodes',
        pageUrl: 'https://www.crunchyroll.com/series/SERIES1/my-show',
        providerSeriesId: 'SERIES1',
        seriesTitle: 'My Show',
        seasonTitle: 'Season 1',
        episodeCardCount: 1,
        watchLinkCount: 1,
        labelledWatchLinkCount: 1,
        observedEpisodeCount: 0,
      });
  });

  it('distinguishes missing season metadata from missing episode links', () => {
    const { document } = parseHTML('<h1>My Show</h1><a href="/watch/EP1/one" aria-label="Play Episode 1 - One"></a>');
    expect(inspectSeriesPage(document, 'https://www.crunchyroll.com/series/SERIES1/my-show').diagnostics.issue)
      .toBe('missing_season_title');
  });

  it('fingerprints the season and rendered provider IDs', () => {
    const { document } = parseHTML(`
      <h1>My Show</h1><button>Season 1</button>
      <article data-t="episode-card "><h3>E1 - One</h3><a href="/watch/EP1/one"></a></article>
    `);
    const observation = inspectSeriesPage(
      document,
      'https://www.crunchyroll.com/series/SERIES1/my-show',
    ).observation;

    expect(observation && catalogObservationFingerprint(observation))
      .toBe('SERIES1||Season 1|EP1');
  });

  it('fingerprints Crunchyroll parts separately even when their season number is shared', () => {
    const observation = {
      schemaVersion: 1 as const,
      provider: 'crunchyroll' as const,
      seriesUrl: 'https://www.crunchyroll.com/series/SERIES1/my-show',
      providerSeriesId: 'SERIES1',
      providerSeasonId: 'PART2',
      seriesTitle: 'My Show',
      seasonTitle: 'Season 1 Part 2',
      seasonNumber: 1,
      episodes: [{
        providerEpisodeId: 'EP13',
        providerUrl: 'https://www.crunchyroll.com/watch/EP13/thirteen',
        episodeNumber: 13,
      }],
      observedAt: '2026-08-08T00:00:00.000Z',
      extensionVersion: '0.1.0',
    };

    expect(catalogObservationFingerprint(observation))
      .toBe('SERIES1|PART2|Season 1 Part 2|EP13');
    expect(catalogObservationFingerprint({
      ...observation,
      providerSeasonId: 'PART3',
      seasonTitle: 'Season 1 Part 3',
    })).not.toBe(catalogObservationFingerprint(observation));
  });
});
