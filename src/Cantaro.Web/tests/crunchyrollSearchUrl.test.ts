import { describe, expect, it } from 'bun:test';
import { getContinueLinkActions } from '../../Cantaro.ClientShared/src/media/pages/media-entry-detail/continueWatchingAction';

describe('getContinueLinkActions', () => {
  it('uses a known series URL when the episode link is unavailable', () => {
    expect(getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'unavailable', episodeNumber: 4 } },
      [{ serviceId: 'netflix', url: 'https://www.netflix.com/title/123', kind: 'series', displayName: 'Netflix' }],
      [],
      'netflix',
      'Title',
    )).toEqual([{
      serviceId: 'netflix',
      url: 'https://www.netflix.com/title/123',
      label: 'Open on Netflix',
      kind: 'series',
    }]);
  });

  it('limits fallback searches to Crunchyrolls working query length', () => {
    expect(getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'unavailable', episodeNumber: 4 } },
      [],
      [],
      null,
      "STEEL BALL RUN JoJo's Bizarre Adventure 1st STAGE",
    )[0]?.url).toBe(
      "https://www.crunchyroll.com/search?q=STEEL%20BALL%20RUN%20JoJo's%20Bizarre%20Ad",
    );
  });

  it('does not modify a query already within the limit', () => {
    expect(getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'unavailable', episodeNumber: 4 } },
      [],
      [],
      null,
      'Black Clover',
    )[0]?.url).toBe('https://www.crunchyroll.com/search?q=Black%20Clover');
  });

  it('uses an episode link on the preferred service before its series link', () => {
    const actions = getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'direct', episodeNumber: 4 } },
      [{ serviceId: 'crunchyroll', url: 'https://www.crunchyroll.com/series/ABC123', kind: 'series', displayName: 'Crunchyroll' }],
      [{ serviceId: 'crunchyroll', url: 'https://www.crunchyroll.com/watch/EP123', kind: 'episode', displayName: 'Crunchyroll' }],
      'crunchyroll',
      'Title',
    );

    expect(actions[0]).toMatchObject({ kind: 'episode', url: 'https://www.crunchyroll.com/watch/EP123' });
  });

  it('prefers a saved series service over another services episode link', () => {
    const actions = getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'direct', episodeNumber: 4 } },
      [{ serviceId: 'netflix', url: 'https://www.netflix.com/title/123', kind: 'series', displayName: 'Netflix' }],
      [{ serviceId: 'crunchyroll', url: 'https://www.crunchyroll.com/watch/EP123', kind: 'episode', displayName: 'Crunchyroll' }],
      'netflix',
      'Title',
    );

    expect(actions.map(action => action.serviceId)).toEqual(['netflix', 'crunchyroll']);
    expect(actions[0]?.kind).toBe('series');
  });

  it('does not expose episode destinations when matching reports a conflict', () => {
    const actions = getContinueLinkActions(
      { status: 'loaded', value: { outcome: 'conflict', episodeNumber: 4 } },
      [{ serviceId: 'netflix', url: 'https://www.netflix.com/title/123', kind: 'series', displayName: 'Netflix' }],
      [{ serviceId: 'crunchyroll', url: 'https://www.crunchyroll.com/watch/EP123', kind: 'episode', displayName: 'Crunchyroll' }],
      'crunchyroll',
      'Title',
    );

    expect(actions).toEqual([{
      serviceId: 'netflix',
      url: 'https://www.netflix.com/title/123',
      label: 'Open on Netflix',
      kind: 'series',
    }]);
  });

  it('retains the server-resolved direct destination', () => {
    const actions = getContinueLinkActions(
      {
        status: 'loaded',
        value: {
          outcome: 'direct',
          episodeNumber: 4,
          provider: 'Crunchyroll',
          url: 'https://www.crunchyroll.com/watch/RESOLVED/episode',
        },
      },
      [],
      [],
      null,
      'Title',
    );

    expect(actions[0]).toMatchObject({
      serviceId: 'crunchyroll',
      url: 'https://www.crunchyroll.com/watch/RESOLVED/episode',
      kind: 'episode',
    });
  });
});
