import {
  MAX_CATALOG_EPISODES_PER_OBSERVATION,
  type CatalogEpisodeObservation,
  type SeriesCatalogObservation,
} from '../../../../contracts/catalogObservation';
import { getExtensionVersion } from '../../../../extensionVersion';
import {
  extractEpisodeId,
  extractEpisodeNumber,
  extractSeasonNumber,
  extractSeriesId,
  parseCrunchyrollUrl,
} from '../shared/crunchyrollUrls';
import { releaseTrackFromSeasonLabel } from '../shared/releaseTrack';

const EPISODE_CARD_SELECTOR = '[data-t^="episode-card"]';
const WATCH_LINK_SELECTOR = 'a[href*="/watch/"]';
const EPISODE_LINK_LABEL_RE = /^(?:(?:play|watch again)\s+)?(?:s\d+\s+)?(?:episode|ep|e)[- _]?\d+\b/i;
const SEASON_LABEL_RE = /^(?:(?:ova|special)\s+)?season\s+\d+(?:\s+part\s+\d+)?$/i;
const SEASON_PREFIX_RE = /^(?:s|season)\s*(\d+)\s*[:|–—-]\s*/i;
const EPISODE_SEASON_PREFIX_RE = /^(?:(?:play|watch again)\s+)?(?:s|season)\s*(\d+)\b/i;
const LANGUAGE_SUFFIX_RE = /\s*\([^()]+?\s+(?:dub|sub)\)\s*$/i;

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

interface SeasonMetadata {
  rawTitle?: string;
  seasonTitle?: string;
  seasonNumber?: number;
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
      extensionVersion: getExtensionVersion(),
    },
  };
}

export function catalogObservationFingerprint(observation: SeriesCatalogObservation): string {
  return [
    observation.providerSeriesId,
    observation.providerSeasonId ?? '',
    observation.seasonTitle,
    ...observation.episodes.flatMap(episode => [
      episode.providerEpisodeId,
      episode.episodeNumber,
      episode.releaseTrack ?? '',
      normalizeProviderUrlForFingerprint(episode.providerUrl),
    ]),
  ].join('|');
}

function normalizeProviderUrlForFingerprint(providerUrl: string): string {
  return parseCrunchyrollUrl(providerUrl)?.href ?? providerUrl;
}

function readSeriesPageState(
  doc: Document,
  locationLike: Location | URL | string,
): SeriesPageState {
  const rawUrl = typeof locationLike === 'string' ? locationLike : locationLike.href;
  const identity = readSeriesIdentity(rawUrl);
  const seriesTitle = readSeriesTitle(doc);
  const links = Array.from(doc.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR));
  const season = readSeasonMetadata(doc, seriesTitle);
  const seasonNumber = season.seasonNumber ?? readConsistentEpisodeSeasonNumber(doc);
  const providerSeasonId = readProviderSeasonId(doc)
    ?? (seasonNumber === undefined && season.seasonTitle
      ? syntheticSeasonSegmentId(season.seasonTitle)
      : undefined);
  const languageAvailability = readSeasonLanguageAvailability(season.rawTitle);
  return {
    pageUrl: identity?.pageUrl.href ?? rawUrl,
    pageUrlValue: identity?.pageUrl ?? null,
    providerSeriesId: identity?.providerSeriesId,
    seriesTitle,
    seasonTitle: season.seasonTitle,
    providerSeasonId,
    seasonNumber,
    episodes: identity
      ? readRenderedEpisodes(doc, identity.pageUrl).map(episode => ({ ...episode, ...languageAvailability }))
      : [],
    episodeCardCount: doc.querySelectorAll(EPISODE_CARD_SELECTOR).length,
    watchLinkCount: links.length,
    labelledWatchLinkCount: links.filter(isLabelledEpisodeLink).length,
  };
}

function readSeasonLanguageAvailability(seasonTitle?: string) {
  const releaseTrack = releaseTrackFromSeasonLabel(seasonTitle);
  if (!releaseTrack) return {};
  const [presentation, languageCode] = releaseTrack.split(':');
  return presentation === 'dub'
    ? { releaseTrack, availableAudioLanguageCodes: [languageCode!] }
    : { releaseTrack, availableSubtitleLanguageCodes: [languageCode!] };
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

function readSeasonMetadata(doc: Document, seriesTitle?: string): SeasonMetadata {
  const attributedTitle = normalizeText(doc.querySelector('[seasontitle]')?.getAttribute('seasontitle'));
  if (attributedTitle) {
    return {
      rawTitle: attributedTitle,
      seasonTitle: attributedTitle,
      seasonNumber: readExplicitSeasonNumber(doc) ?? readSeasonNumberFromText(attributedTitle),
    };
  }

  const rawTitle = [
    readSeasonRawLabel(doc.querySelector('.season-info')),
    readSeasonRawLabel(doc.querySelector('[role="option"][aria-selected="true"]')),
    ...Array.from(doc.querySelectorAll('button, [role="button"]'))
      .filter(button => isTrustedSeasonSelector(button, seriesTitle))
      .map(button => readSeasonRawLabel(button)),
  ].find((value): value is string => Boolean(value));
  const seasonTitle = normalizeFallbackSeasonLabel(rawTitle, seriesTitle);
  return {
    rawTitle,
    seasonTitle,
    seasonNumber: readExplicitSeasonNumber(doc)
      ?? readSeasonNumberFromText(rawTitle)
      ?? readSeasonNumberFromText(seasonTitle),
  };
}

function readProviderSeasonId(doc: Document): string | undefined {
  return normalizeText(doc.querySelector('[currentseasonid]')?.getAttribute('currentseasonid'));
}

function readExplicitSeasonNumber(doc: Document): number | undefined {
  const displayNumber = normalizeText(doc.querySelector('[seasondisplaynumber]')?.getAttribute('seasondisplaynumber'));
  return displayNumber && /^\d+$/.test(displayNumber) ? Number.parseInt(displayNumber, 10) : undefined;
}

function readSeasonNumberFromText(text?: string): number | undefined {
  if (!text) return undefined;
  const prefixed = SEASON_PREFIX_RE.exec(text)?.[1];
  if (prefixed) return Number.parseInt(prefixed, 10);
  return extractSeasonNumber(text);
}

function readSeasonRawLabel(element: Element | null): string | undefined {
  if (!element) return undefined;
  const candidates = [element, ...Array.from(element.querySelectorAll('*'))]
    .map(candidate => normalizeText(candidate.textContent))
    .filter((value): value is string => Boolean(value && !/^\d+\s+episodes?$/i.test(value)))
    .sort((left, right) => left.length - right.length);
  const value = candidates[0];
  return value?.replace(/\s*\d+\s+episodes?\s*$/i, '').trim() || undefined;
}

function isTrustedSeasonSelector(element: Element, seriesTitle?: string): boolean {
  if (element.matches('[aria-haspopup="listbox"], [aria-label="Seasons"]')) return true;
  const value = normalizeText(element.textContent);
  if (!value) return false;
  if (element.matches('[role="button"]')) return false;
  const normalizedValue = value.toLocaleLowerCase();
  const normalizedSeriesTitle = seriesTitle?.toLocaleLowerCase();
  return Boolean(
    SEASON_LABEL_RE.test(value)
      || (normalizedSeriesTitle
        && normalizedValue.startsWith(normalizedSeriesTitle)
        && normalizedValue.length > normalizedSeriesTitle.length),
  );
}

function readConsistentEpisodeSeasonNumber(doc: Document): number | undefined {
  const cards = Array.from(doc.querySelectorAll(EPISODE_CARD_SELECTOR));
  const labels = cards.length > 0
    ? cards.map(card => readEpisodeLabelFromContainer(card))
    : Array.from(doc.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR))
      .filter(isLabelledEpisodeLink)
      .map(link => readEpisodeLabel(link, link));
  const numbers = labels.map(label => label ? readEpisodeSeasonNumber(label) : undefined);
  if (numbers.length === 0 || numbers.some(number => number === undefined)) return undefined;
  const [first] = numbers;
  return numbers.every(number => number === first) ? first : undefined;
}

function readEpisodeSeasonNumber(text: string): number | undefined {
  const value = EPISODE_SEASON_PREFIX_RE.exec(text)?.[1];
  return value ? Number.parseInt(value, 10) : undefined;
}

function readEpisodeLabelFromContainer(container: Element): string | undefined {
  const link = Array.from(container.querySelectorAll<HTMLAnchorElement>(WATCH_LINK_SELECTOR))
    .find(candidate => parseWatchLink(candidate.getAttribute('href'), new URL('https://www.crunchyroll.com/')));
  return link ? readEpisodeLabel(link, container) : undefined;
}

function syntheticSeasonSegmentId(seasonTitle: string): string {
  const languageInsensitiveTitle = seasonTitle.replace(LANGUAGE_SUFFIX_RE, '').trim();
  return `label:${languageInsensitiveTitle}`.slice(0, 256);
}

function normalizeFallbackSeasonLabel(
  value: string | undefined,
  seriesTitle?: string,
): string | undefined {
  if (!value) return undefined;
  const { label, seasonNumber } = stripSeasonLabelDecorations(value);
  if (SEASON_LABEL_RE.test(label)) return label;
  return normalizeSeriesQualifiedSeasonLabel(label, seriesTitle, seasonNumber);
}

function stripSeasonLabelDecorations(value: string): { label: string; seasonNumber?: number } {
  const languageInsensitiveValue = value.replace(LANGUAGE_SUFFIX_RE, '').trim();
  const prefix = SEASON_PREFIX_RE.exec(languageInsensitiveValue);
  return {
    label: prefix
      ? languageInsensitiveValue.slice(prefix[0].length).trim()
      : languageInsensitiveValue,
    seasonNumber: prefix?.[1] ? Number.parseInt(prefix[1], 10) : undefined,
  };
}

function normalizeSeriesQualifiedSeasonLabel(
  label: string,
  seriesTitle: string | undefined,
  seasonNumber: number | undefined,
): string | undefined {
  const fallback = label || numberedSeasonLabel(seasonNumber);
  if (!seriesTitle) return fallback;

  const normalizedSeriesTitle = seriesTitle?.toLocaleLowerCase();
  const comparableValue = label.toLocaleLowerCase();
  if (comparableValue === normalizedSeriesTitle) return numberedSeasonLabel(seasonNumber) ?? 'Season 1';
  if (!comparableValue.startsWith(normalizedSeriesTitle)) return fallback;

  const suffix = label.slice(seriesTitle.length).replace(/^[\s:|–—-]+/, '').trim();
  return suffix || numberedSeasonLabel(seasonNumber) || 'Season 1';
}

function numberedSeasonLabel(seasonNumber: number | undefined): string | undefined {
  return seasonNumber ? `Season ${seasonNumber}` : undefined;
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
  return [link.getAttribute('aria-label'), link.getAttribute('title')]
    .map(normalizeText)
    .some(label => Boolean(label && EPISODE_LINK_LABEL_RE.test(label)));
}

function stripEpisodePrefix(value: string): string | undefined {
  return value
    .replace(/^(?:play|watch again)\s+/i, '')
    .replace(/^(?:s\d+\s+)?(?:episode|ep|e)\s*\d+\s*[-:–—]?\s*/i, '')
    .trim() || undefined;
}

function normalizeText(value: string | null | undefined): string | undefined {
  return value?.replace(/\s+/g, ' ').trim() || undefined;
}
