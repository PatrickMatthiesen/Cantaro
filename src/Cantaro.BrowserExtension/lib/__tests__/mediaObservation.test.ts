import { describe, expect, it } from 'vitest';
import {
  EXTENSION_VERSION,
  SiteIds,
  toSubmitMediaObservationRequest,
  type MediaObservation,
} from '../mediaObservation';

function makeObservation(overrides: Partial<MediaObservation> = {}): MediaObservation {
  return {
    siteId: SiteIds.Crunchyroll,
    observedUrl: 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-1',
    siteMediaId: 'GYVNM7N6Y',
    titleText: 'Episode 1 - My Anime',
    progressHint: 1,
    seriesTitle: 'My Anime',
    episodeTitle: 'The Beginning',
    episodeNumber: 1,
    seasonTitle: 'Season 2',
    seasonNumber: 2,
    watchProgressPercent: 85.5,
    durationSeconds: 1440,
    positionSeconds: 1231.2,
    observedAt: '2026-04-28T10:30:00.000Z',
    extensionVersion: EXTENSION_VERSION,
    ...overrides,
  };
}

describe('toSubmitMediaObservationRequest', () => {
  it('maps extension observations to the backend DTO contract', () => {
    const request = toSubmitMediaObservationRequest(makeObservation());

    expect(request).toEqual({
      siteIdentifier: SiteIds.Crunchyroll,
      observedUrl: 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-1',
      siteMediaId: 'GYVNM7N6Y',
      observedTitle: 'Episode 1 - My Anime',
      seriesTitle: 'My Anime',
      episodeTitle: 'The Beginning',
      episodeNumber: 1,
      seasonTitle: 'Season 2',
      seasonNumber: 2,
      progressHint: '1',
      watchProgressPercent: 85.5,
      durationSeconds: 1440,
      positionSeconds: 1231.2,
      observedAt: '2026-04-28T10:30:00.000Z',
      extensionVersion: EXTENSION_VERSION,
    });
  });

  it('omits progressHint when the adapter could not determine it', () => {
    const request = toSubmitMediaObservationRequest(makeObservation({ progressHint: null }));

    expect(request.progressHint).toBeUndefined();
  });
});
