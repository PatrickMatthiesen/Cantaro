import {
  extractEpisodeId,
  extractEpisodeNumber,
  extractSeasonNumber,
  extractSeriesId,
} from './crunchyrollAdapter';
import {
  EXTENSION_VERSION,
  SiteIds,
  type MediaObservation,
  type ObservedProviderEpisode,
} from './mediaObservation';

const EPISODE_CARD_SELECTOR = '[data-t^="episode-card"]';
const WATCH_LINK_SELECTOR = 'a[href*="/watch/"]';
const EPISODE_LINK_LABEL_RE = /^(?:play|watch again)\s+episode\s+\d+\b/i;
const SEASON_LABEL_RE = /^(?:(?:ova|special)\s+)?season\s+\d+$/i;

export function buildCrunchyrollSeriesObservation(
  doc: Document,
  locationLike: Location | URL | string,
): MediaObservation | null {
  const pageUrl = parseSeriesPageUrl(locationLike);
  if (!pageUrl) return null;

  const providerSeriesId = extractSeriesId(pageUrl.pathname);
  const seriesTitle = readSeriesTitle(doc);
  const seasonTitle = readSelectedSeasonTitle(doc);
  const observedEpisodes = readRenderedEpisodes(doc, pageUrl);
  if (!providerSeriesId || !seriesTitle || !seasonTitle || observedEpisodes.length === 0) return null;

  const qualifiedTitle = qualifySeriesTitle(seriesTitle, seasonTitle);
  const seasonNumber = seasonTitle ? extractSeasonNumber(seasonTitle) : undefined;

  return {
    siteId: SiteIds.Crunchyroll,
    observedUrl: pageUrl.href,
    siteMediaId: buildSeriesObservationId(providerSeriesId, seasonTitle),
    titleText: qualifiedTitle,
    seriesTitle: qualifiedTitle,
    seasonTitle,
    seasonNumber,
    providerSeriesId,
    observedEpisodes,
    progressHint: null,
    observedAt: new Date().toISOString(),
    extensionVersion: EXTENSION_VERSION,
  };
}

export function seriesObservationFingerprint(observation: MediaObservation): string {
  return [
    observation.siteMediaId ?? '',
    ...(observation.observedEpisodes ?? []).map(episode => episode.providerEpisodeId),
  ].join('|');
}

function parseSeriesPageUrl(locationLike: Location | URL | string): URL | null {
  try {
    const url = typeof locationLike === 'string'
      ? new URL(locationLike)
      : new URL(locationLike.href);
    const hostname = url.hostname.toLowerCase();
    if (url.protocol !== 'https:'
      || (hostname !== 'crunchyroll.com' && hostname !== 'www.crunchyroll.com')
      || !extractSeriesId(url.pathname)) {
      return null;
    }
    return url;
  } catch {
    return null;
  }
}

function readSeriesTitle(doc: Document): string | undefined {
  const candidates = [
    doc.querySelector('[data-t="series-title"]'),
    doc.querySelector('[data-testid="series-title"]'),
    doc.querySelector('h1'),
  ];
  return candidates
    .map(element => normalizeText(element?.textContent))
    .find((value): value is string => Boolean(value));
}

function readSelectedSeasonTitle(doc: Document): string | undefined {
  const seasonInfo = readSeasonLabel(doc.querySelector('.season-info'));
  if (seasonInfo) return seasonInfo;

  const selectedOption = doc.querySelector('[role="option"][aria-selected="true"]');
  const selectedText = readSeasonLabel(selectedOption);
  if (selectedText) return selectedText;

  return Array.from(doc.querySelectorAll('button'))
    .map(button => normalizeText(button.textContent))
    .find((value): value is string => Boolean(value && SEASON_LABEL_RE.test(value)));
}

function readSeasonLabel(element: Element | null): string | undefined {
  if (!element) return undefined;

  const childLabel = Array.from(element.children)
    .map(child => normalizeText(child.textContent))
    .find((value): value is string => Boolean(value && SEASON_LABEL_RE.test(value)));
  if (childLabel) return childLabel;

  const value = normalizeText(element.textContent);
  if (!value) return undefined;
  if (SEASON_LABEL_RE.test(value)) return value;

  return /^((?:(?:ova|special)\s+)?season\s+\d+?)\s*\d+\s+episodes?$/i.exec(value)?.[1];
}

function readRenderedEpisodes(doc: Document, pageUrl: URL): ObservedProviderEpisode[] {
  const episodes: ObservedProviderEpisode[] = [];
  const seenProviderIds = new Set<string>();

  for (const card of Array.from(doc.querySelectorAll(EPISODE_CARD_SELECTOR))) {
    const episode = readEpisodeCard(card, pageUrl);
    if (!episode || seenProviderIds.has(episode.providerEpisodeId)) continue;
    seenProviderIds.add(episode.providerEpisodeId);
    episodes.push(episode);
  }

  if (episodes.length === 0) {
    for (const link of Array.from(doc.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR))) {
      const label = normalizeText(link.getAttribute('aria-label'));
      if (!label || !EPISODE_LINK_LABEL_RE.test(label)) continue;

      const episode = readEpisodeLink(link, pageUrl);
      if (!episode || seenProviderIds.has(episode.providerEpisodeId)) continue;
      seenProviderIds.add(episode.providerEpisodeId);
      episodes.push(episode);
    }
  }

  return episodes.slice(0, 100);
}

function readEpisodeCard(card: Element, pageUrl: URL): ObservedProviderEpisode | null {
  const links = Array.from(card.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR));
  const link = links.find(candidate => parseWatchLink(candidate.getAttribute('href'), pageUrl) !== null);
  if (!link) return null;

  const parsedLink = parseWatchLink(link.getAttribute('href'), pageUrl);
  if (!parsedLink) return null;

  const label = readEpisodeLabel(link, card);
  const episodeNumber = label ? extractEpisodeNumber(label) : null;
  if (!episodeNumber || episodeNumber <= 0) return null;

  return {
    ...parsedLink,
    episodeNumber,
    episodeTitle: label ? stripEpisodePrefix(label) : undefined,
  };
}

function readEpisodeLink(link: HTMLAnchorElement, pageUrl: URL): ObservedProviderEpisode | null {
  const parsedLink = parseWatchLink(link.getAttribute('href'), pageUrl);
  if (!parsedLink) return null;

  const label = readEpisodeLabel(link);
  const episodeNumber = label ? extractEpisodeNumber(label) : null;
  if (!episodeNumber || episodeNumber <= 0) return null;

  return {
    ...parsedLink,
    episodeNumber,
    episodeTitle: label ? stripEpisodePrefix(label) : undefined,
  };
}

function parseWatchLink(
  href: string | null,
  pageUrl: URL,
): { providerEpisodeId: string; providerUrl: string } | null {
  if (!href) return null;
  try {
    const url = new URL(href, pageUrl);
    const hostname = url.hostname.toLowerCase();
    const providerEpisodeId = extractEpisodeId(url.pathname);
    if (url.protocol !== 'https:'
      || (hostname !== 'crunchyroll.com' && hostname !== 'www.crunchyroll.com')
      || !providerEpisodeId) {
      return null;
    }
    return { providerEpisodeId, providerUrl: url.href };
  } catch {
    return null;
  }
}

function readEpisodeLabel(link: HTMLAnchorElement, container = link.closest('article, li, [data-t^="episode-card"]')): string | undefined {
  const candidates = [
    link.getAttribute('aria-label'),
    link.getAttribute('title'),
    link.textContent,
    ...Array.from(container?.querySelectorAll('h1, h2, h3, h4') ?? [])
      .map(element => element.textContent),
  ]
    .map(normalizeText)
    .filter((value): value is string => Boolean(value));

  return candidates.find(candidate => extractEpisodeNumber(candidate) !== null);
}

function stripEpisodePrefix(value: string): string | undefined {
  const stripped = value
    .replace(/^(?:play|watch again)\s+/i, '')
    .replace(/^(?:episode|ep|e)\s*\d+\s*[-:–—]?\s*/i, '')
    .trim();
  return stripped || undefined;
}

function qualifySeriesTitle(seriesTitle: string, seasonTitle: string | undefined): string {
  if (!seasonTitle || seriesTitle.toLowerCase().includes(seasonTitle.toLowerCase())) {
    return seriesTitle;
  }
  return `${seriesTitle} ${seasonTitle}`;
}

function buildSeriesObservationId(providerSeriesId: string, seasonTitle: string | undefined): string {
  if (!seasonTitle) return providerSeriesId;
  const seasonKey = seasonTitle
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
  return seasonKey ? `${providerSeriesId}:${seasonKey}` : providerSeriesId;
}

function normalizeText(value: string | null | undefined): string | undefined {
  return value?.replace(/\s+/g, ' ').trim() || undefined;
}
