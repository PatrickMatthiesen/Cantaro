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

export type CrunchyrollSeriesExtractionIssue =
  | 'invalid_series_url'
  | 'missing_series_title'
  | 'missing_season_title'
  | 'no_rendered_episodes';

export interface CrunchyrollSeriesExtractionDiagnostics {
  issue?: CrunchyrollSeriesExtractionIssue;
  pageUrl: string;
  providerSeriesId?: string;
  seriesTitle?: string;
  seasonTitle?: string;
  episodeCardCount: number;
  watchLinkCount: number;
  labelledWatchLinkCount: number;
  observedEpisodeCount: number;
}

export interface CrunchyrollSeriesExtractionResult {
  observation: MediaObservation | null;
  diagnostics: CrunchyrollSeriesExtractionDiagnostics;
}

interface CrunchyrollSeriesPageState {
  pageUrl: URL | null;
  fallbackPageUrl: string;
  providerSeriesId?: string;
  seriesTitle?: string;
  seasonTitle?: string;
  observedEpisodes: ObservedProviderEpisode[];
  episodeCardCount: number;
  watchLinkCount: number;
  labelledWatchLinkCount: number;
}

export function buildCrunchyrollSeriesObservation(
  doc: Document,
  locationLike: Location | URL | string,
): MediaObservation | null {
  return inspectCrunchyrollSeriesPage(doc, locationLike).observation;
}

export function inspectCrunchyrollSeriesPage(
  doc: Document,
  locationLike: Location | URL | string,
): CrunchyrollSeriesExtractionResult {
  const state = readSeriesPageState(doc, locationLike);
  const diagnostics = toExtractionDiagnostics(state);
  const issue = getExtractionIssue(state);
  if (issue) {
    diagnostics.issue = issue;
    return { observation: null, diagnostics };
  }

  return {
    diagnostics,
    observation: buildSeriesObservation(
      state.pageUrl!,
      state.providerSeriesId!,
      state.seriesTitle!,
      state.seasonTitle!,
      state.observedEpisodes,
    ),
  };
}

function buildSeriesObservation(
  pageUrl: URL,
  providerSeriesId: string,
  seriesTitle: string,
  seasonTitle: string,
  observedEpisodes: ObservedProviderEpisode[],
): MediaObservation {
  const qualifiedTitle = qualifySeriesTitle(seriesTitle, seasonTitle);
  return {
    siteId: SiteIds.Crunchyroll,
    observedUrl: pageUrl.href,
    siteMediaId: buildSeriesObservationId(providerSeriesId, seasonTitle),
    titleText: qualifiedTitle,
    seriesTitle: qualifiedTitle,
    seasonTitle,
    seasonNumber: extractSeasonNumber(seasonTitle),
    providerSeriesId,
    observedEpisodes,
    progressHint: null,
    observedAt: new Date().toISOString(),
    extensionVersion: EXTENSION_VERSION,
  };
}

function readSeriesPageState(
  doc: Document,
  locationLike: Location | URL | string,
): CrunchyrollSeriesPageState {
  const pageUrl = parseSeriesPageUrl(locationLike);
  const links = Array.from(doc.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR));
  return {
    pageUrl,
    fallbackPageUrl: readLocationHref(locationLike),
    providerSeriesId: pageUrl ? extractSeriesId(pageUrl.pathname) ?? undefined : undefined,
    seriesTitle: readSeriesTitle(doc),
    seasonTitle: readSelectedSeasonTitle(doc),
    observedEpisodes: pageUrl ? readRenderedEpisodes(doc, pageUrl) : [],
    episodeCardCount: doc.querySelectorAll(EPISODE_CARD_SELECTOR).length,
    watchLinkCount: links.length,
    labelledWatchLinkCount: links.filter(isLabelledEpisodeLink).length,
  };
}

function isLabelledEpisodeLink(link: HTMLAnchorElement): boolean {
  return EPISODE_LINK_LABEL_RE.test(normalizeText(link.getAttribute('aria-label')) ?? '');
}

function toExtractionDiagnostics(state: CrunchyrollSeriesPageState): CrunchyrollSeriesExtractionDiagnostics {
  return {
    pageUrl: state.pageUrl?.href ?? state.fallbackPageUrl,
    providerSeriesId: state.providerSeriesId,
    seriesTitle: state.seriesTitle,
    seasonTitle: state.seasonTitle,
    episodeCardCount: state.episodeCardCount,
    watchLinkCount: state.watchLinkCount,
    labelledWatchLinkCount: state.labelledWatchLinkCount,
    observedEpisodeCount: state.observedEpisodes.length,
  };
}

function getExtractionIssue(state: CrunchyrollSeriesPageState): CrunchyrollSeriesExtractionIssue | undefined {
  if (!state.pageUrl || !state.providerSeriesId) return 'invalid_series_url';
  if (!state.seriesTitle) return 'missing_series_title';
  if (!state.seasonTitle) return 'missing_season_title';
  if (state.observedEpisodes.length === 0) return 'no_rendered_episodes';
  return undefined;
}

function readLocationHref(locationLike: Location | URL | string): string {
  return typeof locationLike === 'string' ? locationLike : locationLike.href;
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
  const cardEpisodes = Array.from(doc.querySelectorAll(EPISODE_CARD_SELECTOR))
    .map(card => readEpisodeCard(card, pageUrl));
  const candidates = cardEpisodes.some(Boolean)
    ? cardEpisodes
    : readLabelledEpisodeLinks(doc, pageUrl);

  return uniqueEpisodes(candidates).slice(0, 100);
}

function readLabelledEpisodeLinks(
  doc: Document,
  pageUrl: URL,
): Array<ObservedProviderEpisode | null> {
  return Array.from(doc.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR))
    .filter(link => EPISODE_LINK_LABEL_RE.test(normalizeText(link.getAttribute('aria-label')) ?? ''))
    .map(link => readEpisodeLink(link, pageUrl));
}

function uniqueEpisodes(
  candidates: Array<ObservedProviderEpisode | null>,
): ObservedProviderEpisode[] {
  const episodes: ObservedProviderEpisode[] = [];
  const seenProviderIds = new Set<string>();

  for (const episode of candidates) {
    if (!episode || seenProviderIds.has(episode.providerEpisodeId)) continue;
    seenProviderIds.add(episode.providerEpisodeId);
    episodes.push(episode);
  }

  return episodes;
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
