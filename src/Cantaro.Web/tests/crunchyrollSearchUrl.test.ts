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

  it('searches Crunchyroll when neither an episode nor series URL is known', () => {
    expect(getContinueLinkAction(
      { status: 'loaded', value: { outcome: 'unavailable', episodeNumber: 4 } },
      null,
      '  That Time I Got Reincarnated as a Slime  ',
    )?.url).toBe(
      'https://www.crunchyroll.com/search?q=That%20Time%20I%20Got%20Reincarnated%20as%20a%20Slime',
    );
  });
});
