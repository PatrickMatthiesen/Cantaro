import { describe, it, expect } from 'vitest';
import {
  extractEpisodeId,
  extractEpisodeNumber,
  parseTitleFromPageTitle,
  buildCrunchyrollObservation,
  extractTitleFromDom,
} from '../crunchyrollAdapter';
import { SiteIds, EXTENSION_VERSION } from '../mediaObservation';

// ---------------------------------------------------------------------------
// extractEpisodeId
// ---------------------------------------------------------------------------

describe('extractEpisodeId', () => {
  it('extracts uppercase alphanumeric ID from a watch URL path', () => {
    expect(extractEpisodeId('/watch/GYVNM7N6Y/my-episode-title')).toBe('GYVNM7N6Y');
  });

  it('extracts ID that contains digits', () => {
    expect(extractEpisodeId('/watch/G14U4G8XY/episode-1')).toBe('G14U4G8XY');
  });

  it('returns undefined for a series URL', () => {
    expect(extractEpisodeId('/series/GY79EN15Y/series-slug')).toBeUndefined();
  });

  it('returns undefined for an empty path', () => {
    expect(extractEpisodeId('')).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// extractEpisodeNumber
// ---------------------------------------------------------------------------

describe('extractEpisodeNumber', () => {
  it('extracts episode number from "episode-N" slug', () => {
    expect(extractEpisodeNumber('/watch/GYVNM7N6Y/episode-5')).toBe(5);
  });

  it('extracts episode number from "ep-N" slug', () => {
    expect(extractEpisodeNumber('/watch/GYVNM7N6Y/ep-12')).toBe(12);
  });

  it('extracts episode number with no separator (ep12)', () => {
    expect(extractEpisodeNumber('/watch/GYVNM7N6Y/ep12')).toBe(12);
  });

  it('returns null when slug has no episode pattern', () => {
    expect(extractEpisodeNumber('/watch/GYVNM7N6Y/prologue')).toBeNull();
  });

  it('returns null for an empty path', () => {
    expect(extractEpisodeNumber('')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// parseTitleFromPageTitle
// ---------------------------------------------------------------------------

describe('parseTitleFromPageTitle', () => {
  it('strips trailing Crunchyroll suffix (dash-separated)', () => {
    const result = parseTitleFromPageTitle('My Episode - My Series - Crunchyroll');
    expect(result).toBe('My Episode - My Series');
  });

  it('strips trailing Crunchyroll suffix (pipe-separated)', () => {
    const result = parseTitleFromPageTitle('My Episode | My Series | Crunchyroll');
    expect(result).toBe('My Episode | My Series');
  });

  it('handles single-segment titles (series page without Crunchyroll suffix)', () => {
    const result = parseTitleFromPageTitle('My Series');
    expect(result).toBe('My Series');
  });

  it('returns empty string for an empty title', () => {
    expect(parseTitleFromPageTitle('')).toBe('');
  });

  it('handles a two-part title without Crunchyroll suffix', () => {
    const result = parseTitleFromPageTitle('Episode - Series');
    expect(result).toBe('Episode - Series');
  });
});

// ---------------------------------------------------------------------------
// extractTitleFromDom
// ---------------------------------------------------------------------------

function makeDoc(
  titlePairs: Array<[selector: string, text: string]>,
): { title: string; querySelector: (sel: string) => { textContent: string } | null } {
  return {
    title: '',
    querySelector(sel: string) {
      const hit = titlePairs.find(([s]) => s === sel);
      return hit ? { textContent: hit[1] } : null;
    },
  };
}

describe('extractTitleFromDom', () => {
  it('returns text from first matching selector', () => {
    const doc = makeDoc([['[data-t="title"]', 'My Series']]);
    expect(extractTitleFromDom(doc)).toBe('My Series');
  });

  it('falls back to lower-priority selector when first is missing', () => {
    const doc = makeDoc([['.title', 'Fallback Title']]);
    expect(extractTitleFromDom(doc)).toBe('Fallback Title');
  });

  it('returns null when no selector matches', () => {
    const doc = makeDoc([]);
    expect(extractTitleFromDom(doc)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// buildCrunchyrollObservation — happy paths
// ---------------------------------------------------------------------------

function makeDocWithTitle(
  pageTitle: string,
  domTitle?: string,
): { title: string; querySelector: (sel: string) => { textContent: string } | null } {
  return {
    title: pageTitle,
    querySelector: domTitle
      ? (sel: string) => (sel === '[data-t="title"]' ? { textContent: domTitle } : null)
      : () => null,
  };
}

describe('buildCrunchyrollObservation — happy paths', () => {
  it('returns a complete observation for a watch URL', () => {
    const url = 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-3-the-beginning';
    const doc = makeDocWithTitle('Episode 3: The Beginning - My Anime - Crunchyroll');

    const obs = buildCrunchyrollObservation(url, doc);

    expect(obs).not.toBeNull();
    expect(obs!.siteId).toBe(SiteIds.Crunchyroll);
    expect(obs!.observedUrl).toBe(url);
    expect(obs!.siteMediaId).toBe('GYVNM7N6Y');
    expect(obs!.progressHint).toBe(3);
    expect(obs!.titleText).toBe('Episode 3: The Beginning - My Anime');
    expect(obs!.extensionVersion).toBe(EXTENSION_VERSION);
    expect(obs!.observedAt).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });

  it('prefers DOM title over page <title> parsing', () => {
    const url = 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-1';
    const doc = makeDocWithTitle('Something Else - Crunchyroll', 'DOM Series Title');

    const obs = buildCrunchyrollObservation(url, doc);
    expect(obs!.titleText).toBe('DOM Series Title');
  });

  it('falls back to page title when DOM has no matching element', () => {
    const url = 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-1';
    const doc = makeDocWithTitle('My Anime - Crunchyroll');

    const obs = buildCrunchyrollObservation(url, doc);
    expect(obs!.titleText).toBe('My Anime');
  });

  it('handles a URL without an episode number in the slug', () => {
    const url = 'https://www.crunchyroll.com/watch/GYVNM7N6Y/prologue';
    const doc = makeDocWithTitle('Prologue - My Anime - Crunchyroll');

    const obs = buildCrunchyrollObservation(url, doc);
    expect(obs!.progressHint).toBeNull();
  });

  it('sets siteMediaId to undefined when watch ID is missing', () => {
    // Malformed watch URL without an ID segment
    const url = 'https://www.crunchyroll.com/watch/episode-only-no-id';
    const doc = makeDocWithTitle('Something - Crunchyroll');

    const obs = buildCrunchyrollObservation(url, doc);
    // This URL still starts with /watch/ so we get an obs but no siteMediaId
    expect(obs).not.toBeNull();
    expect(obs!.siteMediaId).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// buildCrunchyrollObservation — failure paths
// ---------------------------------------------------------------------------

describe('buildCrunchyrollObservation — failure paths', () => {
  it('returns null for a non-Crunchyroll domain', () => {
    const doc = makeDocWithTitle('Some Show - Funimation');
    expect(buildCrunchyrollObservation('https://www.funimation.com/shows/123', doc)).toBeNull();
  });

  it('returns null for a Crunchyroll series page (not a watch URL)', () => {
    const doc = makeDocWithTitle('My Anime - Crunchyroll');
    expect(
      buildCrunchyrollObservation('https://www.crunchyroll.com/series/GY79EN15Y/my-anime', doc),
    ).toBeNull();
  });

  it('returns null for an invalid URL string', () => {
    const doc = makeDocWithTitle('');
    expect(buildCrunchyrollObservation('not-a-url', doc)).toBeNull();
  });

  it('returns null for an empty URL', () => {
    const doc = makeDocWithTitle('');
    expect(buildCrunchyrollObservation('', doc)).toBeNull();
  });

  it('falls back to the raw URL as titleText when page title and DOM are both empty', () => {
    const url = 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-1';
    const doc = makeDocWithTitle('');

    const obs = buildCrunchyrollObservation(url, doc);
    expect(obs!.titleText).toBe(url);
  });
});
