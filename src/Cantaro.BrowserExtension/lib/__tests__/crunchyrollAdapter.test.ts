import { describe, it, expect } from 'vitest';
import {
  buildCrunchyrollObservation,
  extractCrunchyrollEpisodeMetadata,
  extractEpisodeId,
  extractEpisodeNumber,
  extractSeasonNumber,
  extractTitleFromDom,
  parseTitleFromPageTitle,
  trackVideoProgress,
  type DocumentLike,
  type VideoElementLike,
} from '../crunchyrollAdapter';
import { EXTENSION_VERSION, SiteIds } from '../mediaObservation';

function makeDoc(
  title: string,
  textBySelector: Record<string, string> = {},
  videos: VideoElementLike[] = [],
): DocumentLike {
  return {
    title,
    querySelector(selector: string) {
      if (selector === 'video') return videos[0] ?? null;
      const text = textBySelector[selector];
      return text ? { textContent: text } : null;
    },
    querySelectorAll(selector: string) {
      if (selector === 'video') return videos;
      return Object.entries(textBySelector)
        .filter(([key]) => key === selector)
        .map(([, text]) => ({ textContent: text }));
    },
  };
}

class FakeVideo implements VideoElementLike {
  currentTime = 0;
  duration = 100;
  paused = false;
  private readonly listeners = new Map<string, Set<() => void>>();

  addEventListener(type: string, listener: () => void): void {
    const listeners = this.listeners.get(type) ?? new Set<() => void>();
    listeners.add(listener);
    this.listeners.set(type, listeners);
  }

  removeEventListener(type: string, listener: () => void): void {
    this.listeners.get(type)?.delete(listener);
  }

  emit(type: string): void {
    for (const listener of this.listeners.get(type) ?? []) {
      listener();
    }
  }
}

describe('Crunchyroll metadata extraction', () => {
  it('extracts stable watch IDs and episode numbers from watch URLs', () => {
    expect(extractEpisodeId('/watch/GYVNM7N6Y/my-episode-title')).toBe('GYVNM7N6Y');
    expect(extractEpisodeNumber('/watch/GYVNM7N6Y/episode-5')).toBe(5);
    expect(extractEpisodeNumber('E13 - That God\'s Name Is')).toBe(13);
    expect(extractEpisodeNumber('/watch/GYVNM7N6Y/prologue')).toBeNull();
  });

  it('extracts season numbers from season labels', () => {
    expect(extractSeasonNumber('Season 2')).toBe(2);
    expect(extractSeasonNumber('Final Arc')).toBeUndefined();
  });

  it('strips the Crunchyroll suffix from page titles', () => {
    expect(parseTitleFromPageTitle('Episode 3 - My Anime - Crunchyroll')).toBe('Episode 3 - My Anime');
    expect(parseTitleFromPageTitle('Episode 3 | My Anime | Crunchyroll')).toBe('Episode 3 | My Anime');
  });

  it('keeps multi-part fallback episode titles together', () => {
    const metadata = extractCrunchyrollEpisodeMetadata(
      makeDoc('Episode 7 - Like a Fairy Tale - Frieren - Crunchyroll'),
      'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7-like-a-fairy-tale',
    );

    expect(metadata?.seriesTitle).toBe('Frieren');
    expect(metadata?.episodeTitle).toBe('Episode 7 - Like a Fairy Tale');
  });

  it('prefers DOM title selectors over page title fallback', () => {
    const doc = makeDoc('Fallback - Crunchyroll', { '[data-t="title"]': 'DOM Episode Title' });
    expect(extractTitleFromDom(doc)).toBe('DOM Episode Title');
  });

  it('ignores hidden modal titles when extracting the episode title', () => {
    const doc: DocumentLike = {
      title: 'In/Spectre 2 (English Dub) That God\'s Name Is - Watch on Crunchyroll',
      querySelector(selector: string) {
        return this.querySelectorAll?.(selector)?.[0] ?? null;
      },
      querySelectorAll(selector: string) {
        if (selector === 'a[href*="/series/"]') {
          return [{ textContent: 'In/Spectre' }];
        }

        if (selector === '[data-t="title"]') {
          return [
            {
              textContent: 'Please Verify Your Email Address to Continue',
              getBoundingClientRect: () => ({ width: 0, height: 0 }),
            },
          ];
        }

        if (selector === 'h1[class*="title"]') {
          return [
            {
              textContent: 'E13 - That God\'s Name Is',
              getBoundingClientRect: () => ({ width: 400, height: 28 }),
            },
          ];
        }

        return [];
      },
    };

    const metadata = extractCrunchyrollEpisodeMetadata(
      doc,
      'https://www.crunchyroll.com/watch/GN7UDVNXV/that-gods-name-is',
    );

    expect(metadata?.seriesTitle).toBe('In/Spectre');
    expect(metadata?.episodeTitle).toBe('E13 - That God\'s Name Is');
    expect(metadata?.episodeNumber).toBe(13);
  });

  it('returns structured metadata from DOM selectors', () => {
    const doc = makeDoc('Fallback - Crunchyroll', {
      '[data-t="series-title"]': 'Frieren',
      '[data-t="episode-title"]': 'Episode 7 - Like a Fairy Tale',
      '[data-t="season-title"]': 'Season 1',
    });

    const metadata = extractCrunchyrollEpisodeMetadata(
      doc,
      'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7-like-a-fairy-tale',
    );

    expect(metadata).toMatchObject({
      siteId: SiteIds.Crunchyroll,
      observedUrl: 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7-like-a-fairy-tale',
      siteMediaId: 'GYVNM7N6Y',
      titleText: 'Frieren - Episode 7 - Like a Fairy Tale',
      seriesTitle: 'Frieren',
      episodeTitle: 'Episode 7 - Like a Fairy Tale',
      episodeNumber: 7,
      seasonTitle: 'Season 1',
      seasonNumber: 1,
      progressHint: 7,
      extensionVersion: EXTENSION_VERSION,
    });
  });

  it('falls back gracefully when season and episode number are missing', () => {
    const doc = makeDoc('Prologue - My Anime - Crunchyroll');
    const metadata = extractCrunchyrollEpisodeMetadata(
      doc,
      'https://www.crunchyroll.com/watch/GYVNM7N6Y/prologue',
    );

    expect(metadata?.episodeNumber).toBeUndefined();
    expect(metadata?.progressHint).toBeNull();
    expect(metadata?.seasonTitle).toBeUndefined();
    expect(metadata?.titleText).toBe('My Anime - Prologue');
  });

  it('returns null for unsupported URLs', () => {
    const doc = makeDoc('My Anime - Crunchyroll');
    expect(extractCrunchyrollEpisodeMetadata(doc, 'not-a-url')).toBeNull();
    expect(extractCrunchyrollEpisodeMetadata(doc, 'https://www.crunchyroll.com/series/GY79EN15Y/my-anime')).toBeNull();
  });

  it('returns null for Crunchyroll non-episode page states on watch URLs', () => {
    expect(
      extractCrunchyrollEpisodeMetadata(
        makeDoc('Crunchyroll: Watch Popular Anime, Play Games & Shop Online'),
        'https://www.crunchyroll.com/watch/GE00258188ENUS/witch-hat-atelier',
      ),
    ).toBeNull();

    expect(
      extractCrunchyrollEpisodeMetadata(
        makeDoc('Witch Hat Atelier - Please Verify Your Email Address to Continue'),
        'https://www.crunchyroll.com/watch/GE00258188ENUS/witch-hat-atelier',
      ),
    ).toBeNull();
  });

  it('still builds an immediate observation for compatibility callers', () => {
    const doc = makeDoc('Episode 1 - My Anime - Crunchyroll');
    const observation = buildCrunchyrollObservation('https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-1', doc);

    expect(observation?.siteId).toBe(SiteIds.Crunchyroll);
    expect(observation?.observedAt).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });
});

describe('trackVideoProgress', () => {
  const metadata = {
    siteId: SiteIds.Crunchyroll,
    observedUrl: 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7',
    siteMediaId: 'GYVNM7N6Y',
    titleText: 'Frieren - Episode 7',
    episodeNumber: 7,
    progressHint: 7,
    extensionVersion: EXTENSION_VERSION,
  } as const;

  it('fires when playback reaches 85 percent', () => {
    const video = new FakeVideo();
    const observations: unknown[] = [];

    const tracker = trackVideoProgress(makeDoc('', {}, [video]), metadata, (observation) => {
      observations.push(observation);
    });

    expect(tracker).not.toBeNull();
    video.currentTime = 84;
    video.emit('timeupdate');
    expect(observations).toHaveLength(0);

    video.currentTime = 85;
    video.emit('timeupdate');
    expect(observations).toHaveLength(1);
    expect(observations[0]).toMatchObject({
      watchProgressPercent: 85,
      durationSeconds: 100,
      positionSeconds: 85,
      episodeNumber: 7,
      progressHint: 7,
    });
  });

  it('fires only once for the same tracker', () => {
    const video = new FakeVideo();
    const observations: unknown[] = [];
    trackVideoProgress(makeDoc('', {}, [video]), metadata, (observation) => observations.push(observation));

    video.currentTime = 90;
    video.emit('timeupdate');
    video.currentTime = 95;
    video.emit('timeupdate');

    expect(observations).toHaveLength(1);
  });

  it('ignores invalid or zero-duration videos', () => {
    const video = new FakeVideo();
    video.duration = 0;
    video.currentTime = 100;
    const observations: unknown[] = [];

    trackVideoProgress(makeDoc('', {}, [video]), metadata, (observation) => observations.push(observation));
    video.emit('ended');

    expect(observations).toHaveLength(0);
  });

  it('returns null when no video can be found', () => {
    expect(trackVideoProgress(makeDoc(''), metadata, () => null)).toBeNull();
  });
});
