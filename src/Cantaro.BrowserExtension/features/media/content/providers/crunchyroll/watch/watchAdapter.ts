import type { WatchProgressObservation } from '../../../../contracts/watchObservation';
import { getExtensionVersion } from '../../../../extensionVersion';
import {
  extractEpisodeId,
  extractEpisodeNumber,
  extractSeasonNumber,
  extractSeriesId,
  parseCrunchyrollUrl,
} from '../shared/crunchyrollUrls';
import {
  releaseTrackFromPresentationLabel,
  releaseTrackFromProviderEpisodeId,
  releaseTrackFromSelectedPlayerTracks,
  releaseTrackFromSeasonLabel,
} from '../shared/releaseTrack';

export { extractEpisodeId } from '../shared/crunchyrollUrls';

const WATCH_PROGRESS_THRESHOLD = 0.85;
const BLOCKED_PAGE_TITLE_RE = /(?:please verify your email address|watch popular anime|play games|shop online)/i;
const SERIES_SELECTORS = [
  '[data-t="series-title"]',
  '[data-t="show-title"]',
  '[data-testid="series-title"]',
  'a[href*="/series/"]',
];
const EPISODE_SELECTORS = [
  '[data-t="episode-title"]',
  '[data-testid="episode-title"]',
  'h1[class*="title"]',
  'h1',
  '[data-t="title"]',
];
const SEASON_SELECTORS = [
  '[data-t="season-title"]',
  '[data-testid="season-title"]',
  '[class*="season"]',
];
const NEXT_EPISODE_SELECTORS = [
  '[data-t="next-episode"] a[href*="/watch/"]',
  'a[data-t="next-episode"][href*="/watch/"]',
  '[data-testid="next-episode"] a[href*="/watch/"]',
];

export interface CrunchyrollWatchMetadata {
  provider: 'crunchyroll';
  providerEpisodeId: string;
  providerSeriesId?: string;
  observedUrl: string;
  seriesTitle: string;
  episodeTitle: string;
  episodeNumber?: number;
  seasonTitle?: string;
  seasonNumber?: number;
  releaseTrack?: string;
  nextEpisodeProviderId?: string;
  nextEpisodeUrl?: string;
  nextEpisodeTitle?: string;
  nextEpisodeNumber?: number;
  nextEpisodeReleaseTrack?: string;
}

export interface VideoProgressTracker {
  dispose(): void;
}

export type VideoProgressTrackerStatus =
  | { type: 'progress-unavailable'; currentTime: number; duration: number }
  | { type: 'progress'; watchProgressPercent: number; positionSeconds: number; durationSeconds: number }
  | { type: 'threshold-reached'; watchProgressPercent: number; positionSeconds: number; durationSeconds: number };

type ProgressSnapshot = Omit<Extract<VideoProgressTrackerStatus, { type: 'progress' }>, 'type'>;

export function extractCrunchyrollWatchMetadata(
  doc: Document,
  locationLike: Location | URL | string,
  options: { includeCatalogEvidence?: boolean } = {},
): CrunchyrollWatchMetadata | null {
  const rawUrl = typeof locationLike === 'string' ? locationLike : locationLike.href;
  const identity = readWatchIdentity(rawUrl);
  if (!identity) return null;
  const titles = readWatchTitles(doc, identity.pageUrl.pathname);
  if (!titles) return null;
  const seasonTitle = readText(doc, SEASON_SELECTORS)
    ?? seasonTitleFromPageTitle(parsePageTitle(doc.title));
  const releaseTrack = readResolvedWatchReleaseTrack(
    doc,
    identity.providerEpisodeId,
    seasonTitle,
  );
  const nextEpisode = readCatalogNextEpisode(
    doc,
    identity.pageUrl.href,
    releaseTrack,
    options.includeCatalogEvidence,
  );

  return {
    provider: 'crunchyroll',
    providerEpisodeId: identity.providerEpisodeId,
    providerSeriesId: readProviderSeriesId(doc, identity.pageUrl.href),
    observedUrl: identity.pageUrl.href,
    seriesTitle: titles.seriesTitle,
    episodeTitle: titles.episodeTitle,
    episodeNumber: extractEpisodeNumber(`${titles.episodeTitle} ${identity.pageUrl.pathname}`) ?? undefined,
    seasonTitle,
    seasonNumber: extractSeasonNumber(seasonTitle ?? ''),
    releaseTrack,
    nextEpisodeProviderId: nextEpisode?.providerId,
    nextEpisodeUrl: nextEpisode?.url,
    nextEpisodeTitle: nextEpisode?.title,
    nextEpisodeNumber: nextEpisode?.episodeNumber,
    nextEpisodeReleaseTrack: nextEpisode?.releaseTrack,
  };
}

function readCatalogNextEpisode(
  doc: Document,
  pageUrl: string,
  releaseTrack: string | undefined,
  includeCatalogEvidence: boolean | undefined,
) {
  if (includeCatalogEvidence === false) return undefined;
  return readNextEpisode(doc, pageUrl, releaseTrack);
}

function readResolvedWatchReleaseTrack(
  doc: Document,
  providerEpisodeId: string,
  seasonTitle?: string,
) {
  return readSelectedPlayerReleaseTrack(doc)
    ?? readWatchReleaseTrack(providerEpisodeId, seasonTitle, doc.title);
}

function readWatchReleaseTrack(
  providerEpisodeId: string,
  seasonTitle?: string,
  pageTitle?: string,
) {
  return releaseTrackFromProviderEpisodeId(providerEpisodeId)
    ?? releaseTrackFromSeasonLabel(seasonTitle)
    ?? releaseTrackFromSeasonLabel(pageTitle);
}

export function trackVideoProgress(
  doc: Document,
  metadata: CrunchyrollWatchMetadata,
  onThresholdReached: (observation: WatchProgressObservation) => void,
  options: {
    threshold?: number;
    onStatus?: (status: VideoProgressTrackerStatus) => void;
    includeCatalogEvidence?: boolean;
  } = {},
): VideoProgressTracker | null {
  const video = findActiveVideo(doc);
  if (!video) return null;
  const threshold = options.threshold ?? WATCH_PROGRESS_THRESHOLD;
  let fired = false;

  const evaluate = () => {
    if (fired) return;
    const snapshot = readWatchProgress(video);
    if (!snapshot) {
      reportUnavailableProgress(options.onStatus, video);
      return;
    }
    options.onStatus?.({ type: 'progress', ...snapshot });
    if (snapshot.watchProgressPercent / 100 < threshold) return;
    fired = true;
    reportThresholdReached(options.onStatus, snapshot);
    onThresholdReached(buildProgressObservation(doc, metadata, snapshot, options.includeCatalogEvidence));
  };

  video.addEventListener('timeupdate', evaluate);
  video.addEventListener('ended', evaluate);
  video.addEventListener('seeked', evaluate);
  evaluate();
  return {
    dispose() {
      video.removeEventListener('timeupdate', evaluate);
      video.removeEventListener('ended', evaluate);
      video.removeEventListener('seeked', evaluate);
    },
  };
}

function readWatchIdentity(
  rawUrl: string,
): { pageUrl: URL; providerEpisodeId: string } | null {
  const pageUrl = parseCrunchyrollUrl(rawUrl);
  if (!pageUrl) return null;
  const providerEpisodeId = extractEpisodeId(pageUrl.pathname);
  return providerEpisodeId ? { pageUrl, providerEpisodeId } : null;
}

function readWatchTitles(
  doc: Document,
  pathname: string,
): { seriesTitle: string; episodeTitle: string } | null {
  const fallbackTitle = parsePageTitle(doc.title);
  const episodeTitle = readText(doc, EPISODE_SELECTORS) ?? episodeTitleFromPageTitle(fallbackTitle);
  const seriesTitle = readText(doc, SERIES_SELECTORS) ?? seriesTitleFromPageTitle(fallbackTitle);
  if (!episodeTitle || !seriesTitle) return null;
  if (!episodeNumbersAgree(episodeTitle, pathname)) return null;
  return isBlockedPage([fallbackTitle, episodeTitle, seriesTitle])
    ? null
    : { seriesTitle, episodeTitle };
}

function episodeNumbersAgree(episodeTitle: string, pathname: string): boolean {
  const titleNumber = extractEpisodeNumber(episodeTitle);
  const pathNumber = extractEpisodeNumber(pathname);
  if (titleNumber === null || pathNumber === null) return true;
  return titleNumber === pathNumber;
}

function reportUnavailableProgress(
  onStatus: ((status: VideoProgressTrackerStatus) => void) | undefined,
  video: HTMLVideoElement,
): void {
  onStatus?.({
    type: 'progress-unavailable',
    currentTime: video.currentTime,
    duration: video.duration,
  });
}

function reportThresholdReached(
  onStatus: ((status: VideoProgressTrackerStatus) => void) | undefined,
  snapshot: ProgressSnapshot,
): void {
  onStatus?.({ type: 'threshold-reached', ...snapshot });
}

function buildProgressObservation(
  doc: Document,
  metadata: CrunchyrollWatchMetadata,
  snapshot: ProgressSnapshot,
  includeCatalogEvidence = true,
): WatchProgressObservation {
  const refreshed = extractCrunchyrollWatchMetadata(doc, metadata.observedUrl, { includeCatalogEvidence }) ?? metadata;
  return {
    schemaVersion: 1,
    ...refreshed,
    releaseTrack: refreshed.releaseTrack ?? metadata.releaseTrack,
    nextEpisodeReleaseTrack: refreshed.nextEpisodeReleaseTrack
      ?? metadata.nextEpisodeReleaseTrack,
    ...snapshot,
    observedAt: new Date().toISOString(),
    extensionVersion: getExtensionVersion(),
  };
}

export interface SelectedPlayerReleaseTrack {
  audioLabel?: string;
  subtitleLabel?: string;
  releaseTrack?: string;
}

export function readSelectedPlayerTracks(
  doc: Document,
  selectionTarget?: Element,
): SelectedPlayerReleaseTrack {
  const selectedLabel = (menuLabel: string) => {
    const menu = Array.from(doc.querySelectorAll<HTMLElement>('[role="menu"]'))
      .find(element => element.getAttribute('aria-label') === menuLabel);
    const targetItem = selectionTarget?.closest<HTMLElement>('[role="menuitemradio"]');
    if (targetItem && targetItem.closest('[role="menu"]') === menu) {
      return targetItem.getAttribute('aria-label') ?? targetItem.textContent?.trim();
    }
    const selected = menu?.querySelector<HTMLElement>('[role="menuitemradio"][aria-checked="true"]');
    return selected?.getAttribute('aria-label') ?? selected?.textContent?.trim();
  };
  const audioLabel = selectedLabel('Audio Track Selection');
  const subtitleLabel = selectedLabel('Subtitle and closed caption selection');
  return {
    audioLabel,
    subtitleLabel,
    releaseTrack: releaseTrackFromSelectedPlayerTracks(audioLabel, subtitleLabel),
  };
}

export function readSelectedPlayerReleaseTrack(doc: Document): string | undefined {
  return readSelectedPlayerTracks(doc).releaseTrack;
}

function readText(doc: Document, selectors: string[]): string | null {
  for (const selector of selectors) {
    const elements = Array.from(doc.querySelectorAll<HTMLElement>(selector));
    for (const element of elements) {
      const text = element.textContent?.trim();
      if (text && isVisible(element)) return text;
    }
  }
  return null;
}

function isVisible(element: HTMLElement): boolean {
  const rect = element.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
}

function parsePageTitle(pageTitle: string): string {
  const separator = pageTitle.includes(' | ') ? ' | ' : ' - ';
  const parts = pageTitle.split(separator);
  if (parts.at(-1)?.trim().toLowerCase() === 'crunchyroll') parts.pop();
  return parts.join(separator).trim();
}

function titleParts(title: string): string[] {
  const separator = title.includes(' | ') ? ' | ' : ' - ';
  return title.split(separator).map(part => part.trim()).filter(Boolean);
}

function episodeTitleFromPageTitle(title: string): string | undefined {
  const parts = titleParts(title);
  return parts.length > 1 ? parts.slice(0, -1).join(' - ') : title || undefined;
}

function seriesTitleFromPageTitle(title: string): string | undefined {
  const parts = titleParts(title);
  return parts.length > 1 ? parts.at(-1) : undefined;
}

function seasonTitleFromPageTitle(title: string): string | undefined {
  return /\bseason\s+\d+\b/i.exec(title)?.[0];
}

function isBlockedPage(values: string[]): boolean {
  return values.some(value => BLOCKED_PAGE_TITLE_RE.test(value));
}

function readProviderSeriesId(doc: Document, baseUrl: string): string | undefined {
  const href = doc.querySelector<HTMLAnchorElement>('a[href*="/series/"]')?.getAttribute('href');
  const url = href ? parseCrunchyrollUrl(href, baseUrl) : null;
  return url ? extractSeriesId(url.pathname) : undefined;
}

function readNextEpisode(
  doc: Document,
  baseUrl: string,
  currentReleaseTrack?: string,
): { providerId: string; url: string; title?: string; episodeNumber?: number; releaseTrack?: string } | null {
  const link = NEXT_EPISODE_SELECTORS
    .map(selector => doc.querySelector<HTMLAnchorElement>(selector))
    .find((element): element is HTMLAnchorElement => Boolean(element))
    ?? findLabelledNextEpisodeLink(doc);
  if (!link) return null;
  const href = link.getAttribute('href');
  const url = href ? parseCrunchyrollUrl(href, baseUrl) : null;
  const providerId = url ? extractEpisodeId(url.pathname) : undefined;
  if (!url || !providerId) return null;
  const localizedLink = Array.from(doc.querySelectorAll<HTMLAnchorElement>('a[href*="/watch/"]'))
    .find(candidate => {
      const candidateUrl = parseCrunchyrollUrl(candidate.getAttribute('href') ?? '', baseUrl);
      return candidateUrl
        && extractEpisodeId(candidateUrl.pathname) === providerId
        && directNextEpisodeText(candidate) !== undefined;
    });
  const cardText = nextEpisodeCardText(localizedLink ?? link);
  const title = extractNextEpisodeTitle(cardText);
  return {
    providerId,
    url: url.href,
    title,
    episodeNumber: extractEpisodeNumber(`${cardText ?? ''} ${url.pathname}`) ?? undefined,
    releaseTrack: releaseTrackFromProviderEpisodeId(providerId)
      ?? releaseTrackFromPresentationLabel(cardText)
      ?? currentReleaseTrack,
  };
}

function findLabelledNextEpisodeLink(doc: Document): HTMLAnchorElement | null {
  const label = Array.from(doc.querySelectorAll<HTMLElement>('body *'))
    .find(element => /^next episode\s*:?$/i.test(element.textContent?.trim() ?? '') && isVisible(element));
  if (!label) return null;

  let container: HTMLElement | null = label.parentElement;
  for (let depth = 0; container && container !== doc.body && depth < 4; depth += 1) {
    const link = container.querySelector<HTMLAnchorElement>('a[href*="/watch/"]');
    if (link) return link;
    container = container.parentElement;
  }
  return null;
}

function nextEpisodeCardText(link: HTMLAnchorElement): string | undefined {
  const directText = directNextEpisodeText(link);
  if (directText) return directText;

  let container: HTMLElement | null = link.parentElement;
  for (let depth = 0; container && container !== link.ownerDocument.body && depth < 3; depth += 1) {
    const text = container.textContent?.trim();
    if (text && extractEpisodeNumber(text) !== null) return text;
    container = container.parentElement;
  }
  return link.textContent?.trim() || undefined;
}

function directNextEpisodeText(link: HTMLAnchorElement): string | undefined {
  return [
    link.getAttribute('aria-label'),
    link.getAttribute('title'),
    link.textContent,
  ].map(value => value?.trim()).find(value => value && extractEpisodeNumber(value) !== null);
}

function extractNextEpisodeTitle(cardText: string | undefined): string | undefined {
  if (!cardText) return undefined;
  const normalized = cardText
    .replace(/^next episode\s*:?\s*/i, '')
    .replace(/^(?:play|watch again)\s+/i, '')
    .replace(/\s+(?:subtitled|dubbed|dub\s*\|\s*sub|sub\s*\|\s*dub)\s*$/i, '')
    .trim();
  const match = /^(?:episode|ep|e)\s*\d+(?:\.\d+)?\s*[-–—:]\s*(.+)$/i.exec(normalized);
  return match?.[1]?.trim() || normalized || undefined;
}

function findActiveVideo(doc: Document): HTMLVideoElement | null {
  const videos = Array.from(doc.querySelectorAll('video'));
  return videos.find(video => !video.paused) ?? videos[0] ?? null;
}

function readWatchProgress(video: HTMLVideoElement): {
  watchProgressPercent: number;
  durationSeconds: number;
  positionSeconds: number;
} | null {
  if (!Number.isFinite(video.duration) || video.duration <= 0) return null;
  if (!Number.isFinite(video.currentTime) || video.currentTime < 0) return null;
  const positionSeconds = Math.min(video.currentTime, video.duration);
  return {
    watchProgressPercent: Math.min(100, Math.round((positionSeconds / video.duration) * 10_000) / 100),
    durationSeconds: Math.round(video.duration * 100) / 100,
    positionSeconds: Math.round(positionSeconds * 100) / 100,
  };
}
