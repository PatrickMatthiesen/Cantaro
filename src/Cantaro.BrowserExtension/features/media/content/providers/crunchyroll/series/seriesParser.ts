import {
  MAX_CATALOG_EPISODES_PER_OBSERVATION,
  type CatalogEpisodeObservation,
  type SeriesCatalogObservation,
} from '../../../../contracts/catalogObservation';
import {
  extractEpisodeId,
  extractEpisodeNumber,
  extractSeasonNumber,
  extractSeriesId,
  parseCrunchyrollUrl,
} from '../shared/crunchyrollUrls';

const EXTENSION_VERSION = '0.1.0';
const EPISODE_CARD_SELECTOR = '[data-t^="episode-card"]';
const WATCH_LINK_SELECTOR = 'a[href*="/watch/"]';
const EPISODE_LINK_LABEL_RE = /^(?:play|watch again)\s+episode\s+\d+\b/i;
const SEASON_LABEL_RE = /^(?:(?:ova|special)\s+)?season\s+\d+(?:\s+part\s+\d+)?$/i;

export type SeriesExtractionIssue =
  | 'invalid_series_url'
  | 'missing_series_title'
  | 'missing_season_title'
  | 'no_rendered_episodes';

export interface SeriesExtractionDiagnostics {
  issue?: SeriesExtractionIssue;
  pageUrl: string;
  providerSeriesId?: string;
  seriesTitle?: string;
  seasonTitle?: string;
  episodeCardCount: number;
  watchLinkCount: number;
  labelledWatchLinkCount: number;
  observedEpisodeCount: number;
}

export interface SeriesExtractionResult {
  observation: SeriesCatalogObservation | null;
  diagnostics: SeriesExtractionDiagnostics;
}

interface SeriesPageState extends Omit<SeriesExtractionDiagnostics, 'issue' | 'observedEpisodeCount'> {
  pageUrlValue: URL | null;
  providerSeasonId?: string;
  seasonNumber?: number;
  episodes: CatalogEpisodeObservation[];
}

export function inspectSeriesPage(
  doc: Document,
  locationLike: Location | URL | string,
): SeriesExtractionResult {
  const state = readSeriesPageState(doc, locationLike);
  const issue = extractionIssue(state);
  const diagnostics: SeriesExtractionDiagnostics = {
    pageUrl: state.pageUrl,
    providerSeriesId: state.providerSeriesId,
    seriesTitle: state.seriesTitle,
    seasonTitle: state.seasonTitle,
    episodeCardCount: state.episodeCardCount,
    watchLinkCount: state.watchLinkCount,
    labelledWatchLinkCount: state.labelledWatchLinkCount,
    observedEpisodeCount: state.episodes.length,
    issue,
  };
  if (issue || !state.pageUrlValue || !state.providerSeriesId || !state.seriesTitle || !state.seasonTitle) {
    return { observation: null, diagnostics };
  }

  return {
    diagnostics,
    observation: {
      schemaVersion: 1,
      provider: 'crunchyroll',
      seriesUrl: state.pageUrlValue.href,
      providerSeriesId: state.providerSeriesId,
      providerSeasonId: state.providerSeasonId,
      seriesTitle: state.seriesTitle,
      seasonTitle: state.seasonTitle,
      seasonNumber: state.seasonNumber,
      episodes: state.episodes,
      observedAt: new Date().toISOString(),
      extensionVersion: EXTENSION_VERSION,
    },
  };
}

export function catalogObservationFingerprint(observation: SeriesCatalogObservation): string {
  return [
    observation.providerSeriesId,
    observation.providerSeasonId ?? '',
    observation.seasonTitle,
    ...observation.episodes.map(episode => episode.providerEpisodeId),
  ].join('|');
}

function readSeriesPageState(
  doc: Document,
  locationLike: Location | URL | string,
): SeriesPageState {
  const rawUrl = typeof locationLike === 'string' ? locationLike : locationLike.href;
  const identity = readSeriesIdentity(rawUrl);
  const links = Array.from(doc.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR));
  const seasonTitle = readSeasonTitle(doc);
  const languageAvailability = readSeasonLanguageAvailability(seasonTitle);
  return {
    pageUrl: identity?.pageUrl.href ?? rawUrl,
    pageUrlValue: identity?.pageUrl ?? null,
    providerSeriesId: identity?.providerSeriesId,
    seriesTitle: readSeriesTitle(doc),
    seasonTitle,
    providerSeasonId: readProviderSeasonId(doc),
    seasonNumber: readSeasonNumber(doc),
    episodes: identity
      ? readRenderedEpisodes(doc, identity.pageUrl).map(episode => ({ ...episode, ...languageAvailability }))
      : [],
    episodeCardCount: doc.querySelectorAll(EPISODE_CARD_SELECTOR).length,
    watchLinkCount: links.length,
    labelledWatchLinkCount: links.filter(isLabelledEpisodeLink).length,
  };
}

const LANGUAGE_NAME_TO_CODE: Record<string, string> = {
  english: 'en',
  german: 'de',
  spanish: 'es',
  french: 'fr',
  portuguese: 'pt',
  italian: 'it',
  hindi: 'hi',
  arabic: 'ar',
  russian: 'ru',
};

function readSeasonLanguageAvailability(seasonTitle?: string) {
  const match = seasonTitle?.match(/\(([^)]+?)\s+(dub|sub)\)/i);
  if (!match) return {};
  const languageCode = LANGUAGE_NAME_TO_CODE[match[1].trim().toLowerCase()];
  if (!languageCode) return {};
  return match[2].toLowerCase() === 'dub'
    ? { availableAudioLanguageCodes: [languageCode] }
    : { availableSubtitleLanguageCodes: [languageCode] };
}

function readSeriesIdentity(
  rawUrl: string,
): { pageUrl: URL; providerSeriesId: string } | null {
  const pageUrl = parseCrunchyrollUrl(rawUrl);
  if (!pageUrl) return null;
  const providerSeriesId = extractSeriesId(pageUrl.pathname);
  return providerSeriesId ? { pageUrl, providerSeriesId } : null;
}

function extractionIssue(state: SeriesPageState): SeriesExtractionIssue | undefined {
  if (!state.pageUrlValue || !state.providerSeriesId) return 'invalid_series_url';
  if (!state.seriesTitle) return 'missing_series_title';
  if (!state.seasonTitle) return 'missing_season_title';
  if (state.episodes.length === 0) return 'no_rendered_episodes';
  return undefined;
}

function readSeriesTitle(doc: Document): string | undefined {
  const selectors = ['[data-t="series-title"]', '[data-testid="series-title"]', 'h1'];
  return selectors
    .map(selector => normalizeText(doc.querySelector(selector)?.textContent))
    .find((value): value is string => Boolean(value));
}

function readSeasonTitle(doc: Document): string | undefined {
  const attributedTitle = normalizeText(doc.querySelector('[seasontitle]')?.getAttribute('seasontitle'));
  if (attributedTitle) return attributedTitle;
  const seasonInfo = readSeasonLabel(doc.querySelector('.season-info'));
  if (seasonInfo) return seasonInfo;
  const selected = readSeasonLabel(doc.querySelector('[role="option"][aria-selected="true"]'));
  if (selected) return selected;
  return Array.from(doc.querySelectorAll('button'))
    .map(button => normalizeText(button.textContent))
    .find((value): value is string => Boolean(value && SEASON_LABEL_RE.test(value)));
}

function readProviderSeasonId(doc: Document): string | undefined {
  return normalizeText(doc.querySelector('[currentseasonid]')?.getAttribute('currentseasonid'));
}

function readSeasonNumber(doc: Document): number | undefined {
  const displayNumber = normalizeText(doc.querySelector('[seasondisplaynumber]')?.getAttribute('seasondisplaynumber'));
  if (displayNumber && /^\d+$/.test(displayNumber)) return Number.parseInt(displayNumber, 10);
  const title = readSeasonTitle(doc);
  return title ? extractSeasonNumber(title) : undefined;
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
  return /^((?:(?:ova|special)\s+)?season\s+\d+(?:\s+part\s+\d+)?)\s*\d+\s+episodes?$/i.exec(value)?.[1];
}

function readRenderedEpisodes(doc: Document, pageUrl: URL): CatalogEpisodeObservation[] {
  const cards = Array.from(doc.querySelectorAll(EPISODE_CARD_SELECTOR));
  const candidates = cards.length > 0
    ? cards.map(card => readEpisodeCard(card, pageUrl))
    : readLabelledEpisodeLinks(doc, pageUrl);
  return uniqueEpisodes(candidates).slice(0, MAX_CATALOG_EPISODES_PER_OBSERVATION);
}

function readLabelledEpisodeLinks(
  doc: Document,
  pageUrl: URL,
): Array<CatalogEpisodeObservation | null> {
  return Array.from(doc.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR))
    .filter(isLabelledEpisodeLink)
    .map(link => readEpisodeLink(link, link, pageUrl));
}

function readEpisodeCard(card: Element, pageUrl: URL): CatalogEpisodeObservation | null {
  const links = Array.from(card.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR));
  const link = links.find(candidate => parseWatchLink(candidate.getAttribute('href'), pageUrl));
  return link ? readEpisodeLink(link, card, pageUrl) : null;
}

function readEpisodeLink(
  link: HTMLAnchorElement,
  labelContainer: Element,
  pageUrl: URL,
): CatalogEpisodeObservation | null {
  const parsedLink = parseWatchLink(link.getAttribute('href'), pageUrl);
  if (!parsedLink) return null;
  const label = readEpisodeLabel(link, labelContainer);
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
): Pick<CatalogEpisodeObservation, 'providerEpisodeId' | 'providerUrl'> | null {
  if (!href) return null;
  const url = parseCrunchyrollUrl(href, pageUrl.href);
  const providerEpisodeId = url ? extractEpisodeId(url.pathname) : undefined;
  return url && providerEpisodeId
    ? { providerEpisodeId, providerUrl: url.href }
    : null;
}

function readEpisodeLabel(link: HTMLAnchorElement, container: Element): string | undefined {
  return [
    link.getAttribute('aria-label'),
    link.getAttribute('title'),
    link.textContent,
    ...Array.from(container.querySelectorAll('h1, h2, h3, h4')).map(element => element.textContent),
  ]
    .map(normalizeText)
    .filter((value): value is string => Boolean(value))
    .find(candidate => extractEpisodeNumber(candidate) !== null);
}

function uniqueEpisodes(
  candidates: Array<CatalogEpisodeObservation | null>,
): CatalogEpisodeObservation[] {
  const seen = new Set<string>();
  return candidates.filter((episode): episode is CatalogEpisodeObservation => {
    if (!episode || seen.has(episode.providerEpisodeId)) return false;
    seen.add(episode.providerEpisodeId);
    return true;
  });
}

function isLabelledEpisodeLink(link: HTMLAnchorElement): boolean {
  return EPISODE_LINK_LABEL_RE.test(normalizeText(link.getAttribute('aria-label')) ?? '');
}

function stripEpisodePrefix(value: string): string | undefined {
  return value
    .replace(/^(?:play|watch again)\s+/i, '')
    .replace(/^(?:episode|ep|e)\s*\d+\s*[-:–—]?\s*/i, '')
    .trim() || undefined;
}

function normalizeText(value: string | null | undefined): string | undefined {
  return value?.replace(/\s+/g, ' ').trim() || undefined;
}
