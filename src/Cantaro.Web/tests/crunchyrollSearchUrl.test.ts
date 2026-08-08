import { describe, expect, it } from 'bun:test';
import { getContinueLinkAction } from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/continueWatchingAction';

describe('getContinueLinkAction', () => {
  it('uses a known series URL when the episode link is unavailable', () => {
    expect(getContinueLinkAction(
      { status: 'loaded', value: { outcome: 'unavailable', episodeNumber: 4 } },
      'https://www.crunchyroll.com/series/ABC123/title',
      'Title',
    )).toEqual({
      url: 'https://www.crunchyroll.com/series/ABC123/title',
      label: 'Open series on Crunchyroll',
      isEpisodeLink: false,
    });
  });

  it('limits fallback searches to Crunchyrolls working query length', () => {
    expect(getContinueLinkAction(
      { status: 'loaded', value: { outcome: 'unavailable', episodeNumber: 4 } },
      null,
      "STEEL BALL RUN JoJo's Bizarre Adventure 1st STAGE",
    )?.url).toBe(
      "https://www.crunchyroll.com/search?q=STEEL%20BALL%20RUN%20JoJo's%20Bizarre%20Ad",
    );
  });

  it('does not modify a query already within the limit', () => {
    expect(getContinueLinkAction(
      { status: 'loaded', value: { outcome: 'unavailable', episodeNumber: 4 } },
      null,
      'Black Clover',
    )?.url).toBe('https://www.crunchyroll.com/search?q=Black%20Clover');
  });
});
