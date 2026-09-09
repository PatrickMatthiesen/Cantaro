import { parseHTML } from 'linkedom';
import { describe, expect, it } from 'vitest';
import type { WatchProgressObservation } from '../../../../contracts/watchObservation';
import wistoriaSeasonTwoEpisodeTwentyFourHtml from './fixtures/wistoria-season-2-episode-24.html?raw';
import nextEpisodeJapaneseCardHtml from './fixtures/next-episode-japanese-card.html?raw';
import opaqueEnglishDubWatchHtml from './fixtures/opaque-english-dub-watch.html?raw';
import {
  extractCrunchyrollWatchMetadata,
  readSelectedPlayerReleaseTrack,
  readSelectedPlayerTracks,
  trackVideoProgress,
  type CrunchyrollWatchMetadata,
} from './watchAdapter';

interface ElementLike {
  textContent: string | null;
  getBoundingClientRect(): { width: number; height: number };
  getAttribute?(name: string): string | null;
}

function textElement(text: string, visible = true): ElementLike {
  return {
    textContent: text,
    getBoundingClientRect: () => visible
      ? { width: 400, height: 28 }
      : { width: 0, height: 0 },
  };
}

function linkElement(text: string, href: () => string | null): ElementLike {
  return {
    ...textElement(text),
    getAttribute: name => name === 'href' ? href() : null,
  };
}

function makeDoc(
  title: string,
  elements: Record<string, ElementLike[]> = {},
  videos: FakeVideo[] = [],
): Document {
  const { document } = parseHTML(`<html><head><title>${title}</title></head><body></body></html>`);
  const nativeQuerySelector = document.querySelector.bind(document);
  const nativeQuerySelectorAll = document.querySelectorAll.bind(document);
  Object.defineProperty(document, 'querySelector', {
    value(selector: string) {
      if (selector === 'video') return videos[0] ?? null;
      return elements[selector]?.[0] ?? nativeQuerySelector(selector);
    },
  });
  Object.defineProperty(document, 'querySelectorAll', {
    value(selector: string) {
      if (selector === 'video') return videos;
      return elements[selector] ?? nativeQuerySelectorAll(selector);
    },
  });
  return document;
}

function fixtureDoc(html: string): Document {
  const { document } = parseHTML(html);
  for (const element of document.querySelectorAll<HTMLElement>('*')) {
    Object.defineProperty(element, 'getBoundingClientRect', {
      value: () => ({ width: 400, height: 28 }),
    });
  }
  return document;
}

class FakeVideo {
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
    for (const listener of this.listeners.get(type) ?? []) listener();
  }
}

describe('Crunchyroll watch metadata extraction', () => {
  it('reads the selected player audio and subtitle tracks from their checked menu items', () => {
    const { document: doc } = parseHTML(`
      <div role="menu" aria-label="Audio Track Selection">
        <div role="menuitemradio" aria-label="Japanese" aria-checked="true">Japanese</div>
      </div>
      <div role="menu" aria-label="Subtitle and closed caption selection">
        <div role="menuitemradio" aria-label="English" aria-checked="true">English</div>
      </div>
    `);

    expect(readSelectedPlayerReleaseTrack(doc)).toBe('sub:en');
  });

  it('reads the clicked audio choice before Crunchyroll updates and closes a multi-option menu', () => {
    const { document: doc } = parseHTML(`
      <div role="menu" aria-label="Audio Track Selection">
        <div role="menuitemradio" aria-label="Japanese">Japanese</div>
        <div role="menuitemradio" aria-label="English">English</div>
        <div role="menuitemradio" aria-label="Deutsch" aria-checked="true">Deutsch</div>
      </div>
      <div role="menu" aria-label="Subtitle and closed caption selection">
        <div role="menuitemradio" aria-label="Deutsch">Deutsch</div>
        <div role="menuitemradio" aria-label="None" aria-checked="true">None</div>
      </div>
    `);
    const englishChoice = doc.querySelector('[aria-label="English"]');

    expect(readSelectedPlayerTracks(doc, englishChoice ?? undefined)).toEqual({
      audioLabel: 'English',
      subtitleLabel: 'None',
      releaseTrack: 'dub:en',
    });
    expect(readSelectedPlayerReleaseTrack(doc)).toBe('dub:de');
  });

  it('extracts the cumulative Wistoria episode from the sanitized Crunchyroll DOM contract', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      fixtureDoc(wistoriaSeasonTwoEpisodeTwentyFourHtml),
      'https://www.crunchyroll.com/watch/GE00340382ENUS/a-story-of-a-dream-with-no-end',
    );

    expect(metadata).toMatchObject({
      releaseTrack: 'dub:en',
      providerEpisodeId: 'GE00340382ENUS',
      providerSeriesId: 'GW4HM7WK9',
      seriesTitle: 'Wistoria: Wand and Sword',
      episodeTitle: 'E24 - A Story of a Dream with No End',
      episodeNumber: 24,
      seasonTitle: 'Season 2',
      seasonNumber: 2,
    });
    expect(metadata?.nextEpisodeProviderId).toBeUndefined();
  });

  it('builds the API threshold observation from the sanitized Crunchyroll DOM contract', () => {
    const doc = fixtureDoc(wistoriaSeasonTwoEpisodeTwentyFourHtml);
    const metadata = extractCrunchyrollWatchMetadata(
      doc,
      'https://www.crunchyroll.com/watch/GE00340382ENUS/a-story-of-a-dream-with-no-end',
    );
    expect(metadata).not.toBeNull();

    const video = doc.querySelector('video');
    expect(video).not.toBeNull();
    Object.defineProperties(video, {
      currentTime: { value: 1353.32302, writable: true },
      duration: { value: 1426.051, writable: true },
      paused: { value: false, writable: true },
    });

    let observation: WatchProgressObservation | undefined;
    const tracker = trackVideoProgress(
      doc,
      metadata!,
      value => { observation = value; },
    );

    expect(observation).toMatchObject({
      provider: 'crunchyroll',
      providerEpisodeId: 'GE00340382ENUS',
      providerSeriesId: 'GW4HM7WK9',
      seriesTitle: 'Wistoria: Wand and Sword',
      episodeTitle: 'E24 - A Story of a Dream with No End',
      episodeNumber: 24,
      seasonTitle: 'Season 2',
      seasonNumber: 2,
      watchProgressPercent: 94.9,
      positionSeconds: 1353.32,
      durationSeconds: 1426.05,
    });
    tracker?.dispose();
  });

  it('keeps multi-part fallback episode titles together', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      makeDoc('Episode 7 - Like a Fairy Tale - Frieren - Crunchyroll'),
      'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7-like-a-fairy-tale',
    );

    expect(metadata?.seriesTitle).toBe('Frieren');
    expect(metadata?.episodeTitle).toBe('Episode 7 - Like a Fairy Tale');
  });

  it('prefers visible DOM titles over the page-title fallback', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      makeDoc('Fallback Episode - Fallback Series - Crunchyroll', {
        '[data-t="series-title"]': [textElement('Frieren')],
        '[data-t="episode-title"]': [textElement('Episode 7 - Like a Fairy Tale')],
      }),
      'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7',
    );

    expect(metadata).toMatchObject({
      seriesTitle: 'Frieren',
      episodeTitle: 'Episode 7 - Like a Fairy Tale',
      episodeNumber: 7,
    });
  });

  it('ignores hidden modal titles', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      makeDoc('In/Spectre 2 - That God\'s Name Is - Crunchyroll', {
        'a[href*="/series/"]': [linkElement('In/Spectre', () => '/series/INSPECTRE/show')],
        '[data-t="title"]': [textElement('Please Verify Your Email Address to Continue', false)],
        'h1[class*="title"]': [textElement('E13 - That God\'s Name Is')],
      }),
      'https://www.crunchyroll.com/watch/GN7UDVNXV/that-gods-name-is',
    );

    expect(metadata).toMatchObject({
      seriesTitle: 'In/Spectre',
      episodeTitle: 'E13 - That God\'s Name Is',
      episodeNumber: 13,
    });
  });

  it('returns structured metadata from provider selectors', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      makeDoc('Fallback - Crunchyroll', {
        '[data-t="series-title"]': [textElement('Frieren')],
        '[data-t="episode-title"]': [textElement('Episode 7 - Like a Fairy Tale')],
        '[data-t="season-title"]': [textElement('Season 1')],
      }),
      'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7-like-a-fairy-tale',
    );

    expect(metadata).toMatchObject({
      provider: 'crunchyroll',
      observedUrl: 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7-like-a-fairy-tale',
      providerEpisodeId: 'GYVNM7N6Y',
      seriesTitle: 'Frieren',
      episodeTitle: 'Episode 7 - Like a Fairy Tale',
      episodeNumber: 7,
      seasonTitle: 'Season 1',
      seasonNumber: 1,
    });
  });

  it('captures rendered series and next-episode links', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      makeDoc('Episode 7 - Frieren - Crunchyroll', {
        'a[href*="/series/"]': [linkElement('Frieren', () => '/series/GYEXQKJG6/frieren')],
        '[data-t="next-episode"] a[href*="/watch/"]': [
          linkElement('Episode 8 - The Hero of the Village', () => '/watch/G31UXQ9K2/episode-8'),
        ],
      }),
      'https://www.crunchyroll.com/da/watch/CURRENT7/episode-7',
    );

    expect(metadata).toMatchObject({
      providerSeriesId: 'GYEXQKJG6',
      nextEpisodeProviderId: 'G31UXQ9K2',
      nextEpisodeUrl: 'https://www.crunchyroll.com/watch/G31UXQ9K2/episode-8',
      nextEpisodeNumber: 8,
    });
  });

  it('does not collect next-episode catalog evidence when catalog consent is disabled', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      makeDoc('Episode 7 - Frieren - Crunchyroll', {
        'a[href*="/series/"]': [linkElement('Frieren', () => '/series/GYEXQKJG6/frieren')],
        '[data-t="next-episode"] a[href*="/watch/"]': [
          linkElement('Episode 8 - The Hero of the Village', () => '/watch/G31UXQ9K2/episode-8'),
        ],
      }),
      'https://www.crunchyroll.com/watch/CURRENT7/episode-7',
      { includeCatalogEvidence: false },
    );

    expect(metadata).toMatchObject({
      providerEpisodeId: 'CURRENT7',
      providerSeriesId: 'GYEXQKJG6',
    });
    expect(metadata?.nextEpisodeProviderId).toBeUndefined();
    expect(metadata?.nextEpisodeUrl).toBeUndefined();
  });

  it('captures the visible labelled next-episode card with its real title', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      fixtureDoc(nextEpisodeJapaneseCardHtml),
      'https://www.crunchyroll.com/watch/GE00374382JAJP/the-day-of-departure',
    );

    expect(metadata).toMatchObject({
      nextEpisodeProviderId: 'GE00374383JAJP',
      nextEpisodeUrl: 'https://www.crunchyroll.com/watch/GE00374383JAJP/death-and-loss',
      nextEpisodeNumber: 19,
      nextEpisodeTitle: 'Death and Loss',
      nextEpisodeReleaseTrack: 'sub:en',
    });
  });

  it('reads the selected dub and sibling next-card metadata for opaque episode IDs', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      fixtureDoc(opaqueEnglishDubWatchHtml),
      'https://www.crunchyroll.com/watch/GVWU8X9GQ/a-ghost-may-show-up-during-training-but-nobody-told-me-about-it',
    );

    expect(metadata).toMatchObject({
      providerEpisodeId: 'GVWU8X9GQ',
      releaseTrack: 'dub:en',
      nextEpisodeProviderId: 'GEVUW3NPM',
      nextEpisodeNumber: 11,
      nextEpisodeTitle: 'It May Be a Trap, but I Have to Keep Moving Forward',
      nextEpisodeReleaseTrack: 'dub:en',
    });
  });

  it('falls back gracefully when season and episode number are missing', () => {
    const metadata = extractCrunchyrollWatchMetadata(
      makeDoc('Prologue - My Anime - Crunchyroll'),
      'https://www.crunchyroll.com/watch/GYVNM7N6Y/prologue',
    );

    expect(metadata).toMatchObject({
      seriesTitle: 'My Anime',
      episodeTitle: 'Prologue',
    });
    expect(metadata?.episodeNumber).toBeUndefined();
    expect(metadata?.seasonTitle).toBeUndefined();
  });

  it('returns null for unsupported URLs', () => {
    const doc = makeDoc('My Anime - Crunchyroll');
    expect(extractCrunchyrollWatchMetadata(doc, 'not-a-url')).toBeNull();
    expect(extractCrunchyrollWatchMetadata(doc, 'https://www.crunchyroll.com/series/GY79EN15Y/my-anime')).toBeNull();
    expect(extractCrunchyrollWatchMetadata(doc, 'https://evilcrunchyroll.com/watch/GYVNM7N6Y/episode-1')).toBeNull();
  });

  it('returns null for non-episode states on watch URLs', () => {
    expect(extractCrunchyrollWatchMetadata(
      makeDoc('Crunchyroll: Watch Popular Anime, Play Games & Shop Online'),
      'https://www.crunchyroll.com/watch/GE00258188ENUS/witch-hat-atelier',
    )).toBeNull();
    expect(extractCrunchyrollWatchMetadata(
      makeDoc('Witch Hat Atelier - Please Verify Your Email Address to Continue'),
      'https://www.crunchyroll.com/watch/GE00258188ENUS/witch-hat-atelier',
    )).toBeNull();
  });

  it('does not bind a new SPA watch URL to stale episode DOM metadata', () => {
    const staleDocument = makeDoc('Episode 7 - Example Show - Crunchyroll', {
      '[data-t="series-title"]': [textElement('Example Show')],
      '[data-t="episode-title"]': [textElement('Episode 7 - Old DOM')],
    });

    expect(extractCrunchyrollWatchMetadata(
      staleDocument,
      'https://www.crunchyroll.com/watch/EPISODE8/episode-8-new-url',
    )).toBeNull();
  });
});

describe('trackVideoProgress', () => {
  const metadata: CrunchyrollWatchMetadata = {
    provider: 'crunchyroll',
    observedUrl: 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-7',
    providerEpisodeId: 'GYVNM7N6Y',
    seriesTitle: 'Frieren',
    episodeTitle: 'Episode 7',
    episodeNumber: 7,
  };

  it('fires when playback reaches 85 percent', () => {
    const video = new FakeVideo();
    const observations: WatchProgressObservation[] = [];
    const tracker = trackVideoProgress(
      makeDoc('Episode 7 - Frieren - Crunchyroll', {}, [video]),
      metadata,
      observation => observations.push(observation),
    );

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
      schemaVersion: 1,
    });
  });

  it('fires only once for one tracker', () => {
    const video = new FakeVideo();
    const observations: WatchProgressObservation[] = [];
    trackVideoProgress(
      makeDoc('Episode 7 - Frieren - Crunchyroll', {}, [video]),
      metadata,
      observation => observations.push(observation),
    );

    video.currentTime = 90;
    video.emit('timeupdate');
    video.currentTime = 95;
    video.emit('timeupdate');
    expect(observations).toHaveLength(1);
  });

  it('refreshes a next link rendered after tracking was armed', () => {
    const video = new FakeVideo();
    let nextHref: string | null = null;
    const doc = makeDoc('Episode 7 - Frieren - Crunchyroll', {
      '[data-t="next-episode"] a[href*="/watch/"]': [
        linkElement('Episode 8', () => nextHref),
      ],
    }, [video]);
    const observations: WatchProgressObservation[] = [];
    trackVideoProgress(doc, metadata, observation => observations.push(observation));

    nextHref = '/watch/NEXT8/episode-8';
    video.currentTime = 90;
    video.emit('timeupdate');

    expect(observations[0]).toMatchObject({
      nextEpisodeProviderId: 'NEXT8',
      nextEpisodeUrl: 'https://www.crunchyroll.com/watch/NEXT8/episode-8',
      nextEpisodeNumber: 8,
    });
  });

  it('reports but does not submit invalid or zero-duration progress', () => {
    const video = new FakeVideo();
    video.duration = 0;
    video.currentTime = 100;
    const observations: WatchProgressObservation[] = [];
    const statuses: string[] = [];
    trackVideoProgress(
      makeDoc('Episode 7 - Frieren - Crunchyroll', {}, [video]),
      metadata,
      observation => observations.push(observation),
      { onStatus: status => statuses.push(status.type) },
    );
    video.emit('ended');

    expect(observations).toHaveLength(0);
    expect(statuses).toContain('progress-unavailable');
  });

  it('returns null when no video can be found', () => {
    expect(trackVideoProgress(makeDoc('Episode 7 - Frieren - Crunchyroll'), metadata, () => undefined))
      .toBeNull();
  });

  it('removes progress listeners when disposed', () => {
    const video = new FakeVideo();
    const observations: WatchProgressObservation[] = [];
    const tracker = trackVideoProgress(
      makeDoc('Episode 7 - Frieren - Crunchyroll', {}, [video]),
      metadata,
      observation => observations.push(observation),
    );
    tracker?.dispose();
    video.currentTime = 90;
    video.emit('timeupdate');
    expect(observations).toHaveLength(0);
  });
});
