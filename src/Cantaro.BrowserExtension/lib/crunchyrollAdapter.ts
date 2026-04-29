import { SiteIds, EXTENSION_VERSION } from './mediaObservation';
import type { MediaObservation } from './mediaObservation';

/** Minimal document interface so the adapter can be unit-tested without a real DOM. */
export interface DocumentLike {
  title: string;
  querySelector(selector: string): { textContent: string | null } | null;
}

/**
 * Regex for extracting the episode/media ID from a Crunchyroll watch URL.
 * Example: https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-slug
 *   → "GYVNM7N6Y"
 */
const WATCH_ID_RE = /\/watch\/([A-Z0-9]+)\//i;

/** Regex for pulling an episode number out of a URL slug, e.g. "episode-1", "ep-12". */
const EPISODE_NUMBER_RE = /(?:episode|ep)[- _]?(\d+)/i;

/**
 * Extract the Crunchyroll episode/media ID from a URL pathname.
 * Returns undefined when the URL is not a watch page or the ID is absent.
 */
export function extractEpisodeId(pathname: string): string | undefined {
  return WATCH_ID_RE.exec(pathname)?.[1];
}

/**
 * Extract an episode number hint from a URL slug.
 * Returns null when not determinable.
 */
export function extractEpisodeNumber(pathname: string): number | null {
  const slug = pathname.split('/').pop() ?? '';
  const match = EPISODE_NUMBER_RE.exec(slug);
  return match ? parseInt(match[1], 10) : null;
}

/**
 * Parse a human-readable title from the page <title> element.
 *
 * Crunchyroll uses several formats depending on the view:
 *   "Episode Title - Series Name - Crunchyroll"  (watch page, dash-separated)
 *   "Series Name - Crunchyroll"                  (series page)
 * Newer versions sometimes use " | " as the separator instead.
 *
 * We strip the trailing " - Crunchyroll" / " | Crunchyroll" suffix and return
 * the meaningful portion.
 */
export function parseTitleFromPageTitle(pageTitle: string): string {
  if (!pageTitle) return '';

  // Detect separator (prefer " | " over " - " when both are present)
  const separator = pageTitle.includes(' | ') ? ' | ' : ' - ';
  const parts = pageTitle.split(separator);

  // Remove the "Crunchyroll" brand suffix from the end
  if (parts.length > 1 && parts[parts.length - 1].trim().toLowerCase() === 'crunchyroll') {
    parts.pop();
  }

  return parts.join(separator).trim();
}

/**
 * Attempt to extract a richer series title from known DOM selectors.
 * Returns null when no matching element is found so the caller can fall back
 * to the page title.
 */
export function extractTitleFromDom(doc: DocumentLike): string | null {
  // Crunchyroll's watch page renders series/episode headings in these elements.
  // Selectors are tried in preference order; the first non-empty result wins.
  const candidates = [
    '[data-t="title"]',
    '.title',
    'h1[class*="title"]',
    'h1',
  ];

  for (const selector of candidates) {
    const el = doc.querySelector(selector);
    const text = el?.textContent?.trim();
    if (text) return text;
  }

  return null;
}

/**
 * Build a complete MediaObservation for a Crunchyroll watch page.
 * Returns null when the URL is not a supported Crunchyroll watch URL.
 */
export function buildCrunchyrollObservation(
  url: string,
  doc: DocumentLike,
): MediaObservation | null {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }

  if (!parsed.hostname.endsWith('crunchyroll.com')) return null;
  if (!parsed.pathname.startsWith('/watch/')) return null;

  const siteMediaId = extractEpisodeId(parsed.pathname);
  const progressHint = extractEpisodeNumber(parsed.pathname);

  // Prefer DOM title, fall back to <title> parsing
  const domTitle = extractTitleFromDom(doc);
  const titleText = domTitle ?? parseTitleFromPageTitle(doc.title);

  return {
    siteId: SiteIds.Crunchyroll,
    observedUrl: url,
    siteMediaId,
    titleText: titleText || url,
    progressHint,
    observedAt: new Date().toISOString(),
    extensionVersion: EXTENSION_VERSION,
  };
}
