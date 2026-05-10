import { SiteIds, EXTENSION_VERSION } from './mediaObservation';
import type { MediaObservation } from './mediaObservation';

const WATCH_PROGRESS_THRESHOLD = 0.85;

export interface TextElementLike {
  textContent: string | null;
}

export interface VideoElementLike {
  currentTime: number;
  duration: number;
  paused?: boolean;
  addEventListener(type: string, listener: () => void): void;
  removeEventListener(type: string, listener: () => void): void;
}

export interface DocumentLike {
  title: string;
  querySelector(selector: string): TextElementLike | VideoElementLike | null;
  querySelectorAll?(selector: string): Array<TextElementLike | VideoElementLike>;
}

export interface LocationLike {
  href: string;
  hostname: string;
  pathname: string;
}

export interface CrunchyrollEpisodeMetadata {
  siteId: typeof SiteIds.Crunchyroll;
  observedUrl: string;
  siteMediaId?: string;
  titleText: string;
  seriesTitle?: string;
  episodeTitle?: string;
  episodeNumber?: number;
  seasonTitle?: string;
  seasonNumber?: number;
  progressHint?: number | null;
  extensionVersion: string;
}

interface WatchProgressSnapshot {
  watchProgressPercent: number;
  durationSeconds: number;
  positionSeconds: number;
}

export interface VideoProgressTracker {
  dispose(): void;
}

const WATCH_ID_RE = /\/watch\/([A-Z0-9]+)(?:\/|$)/i;
const EPISODE_NUMBER_RE = /(?:episode|ep)[- _]?(\d+)/i;
const SEASON_NUMBER_RE = /season\s*(\d+)/i;

const SERIES_SELECTORS = [
  '[data-t="series-title"]',
  '[data-t="show-title"]',
  '[data-testid="series-title"]',
  'a[href*="/series/"]',
];

const EPISODE_SELECTORS = [
  '[data-t="episode-title"]',
  '[data-t="title"]',
  '[data-testid="episode-title"]',
  'h1[class*="title"]',
  'h1',
];

const SEASON_SELECTORS = [
  '[data-t="season-title"]',
  '[data-testid="season-title"]',
  '[class*="season"]',
];

export function extractEpisodeId(pathname: string): string | undefined {
  return WATCH_ID_RE.exec(pathname)?.[1];
}

export function extractEpisodeNumber(text: string): number | null {
  const match = EPISODE_NUMBER_RE.exec(text);
  return match ? parseInt(match[1], 10) : null;
}

export function extractSeasonNumber(text: string): number | undefined {
  const match = SEASON_NUMBER_RE.exec(text);
  return match ? parseInt(match[1], 10) : undefined;
}

export function parseTitleFromPageTitle(pageTitle: string): string {
  if (!pageTitle) return '';

  const separator = pageTitle.includes(' | ') ? ' | ' : ' - ';
  const parts = pageTitle.split(separator);

  if (parts.length > 1 && parts[parts.length - 1].trim().toLowerCase() === 'crunchyroll') {
    parts.pop();
  }

  return parts.join(separator).trim();
}

function extractTextFromDom(doc: DocumentLike, selectors: string[]): string | null {
  for (const selector of selectors) {
    const element = doc.querySelector(selector);
    const text = 'textContent' in (element ?? {}) ? element?.textContent?.trim() : '';
    if (text) return text;
  }

  return null;
}

export function extractTitleFromDom(doc: DocumentLike): string | null {
  return extractTextFromDom(doc, EPISODE_SELECTORS);
}

export function extractCrunchyrollEpisodeMetadata(
  doc: DocumentLike,
  locationLike: LocationLike | string,
): CrunchyrollEpisodeMetadata | null {
  const parsed = parseLocation(locationLike);
  if (!parsed) return null;
  if (!parsed.hostname.endsWith('crunchyroll.com')) return null;
  if (!parsed.pathname.startsWith('/watch/')) return null;

  const siteMediaId = extractEpisodeId(parsed.pathname);
  const titleFallback = parseTitleFromPageTitle(doc.title);
  const episodeTitle = extractTextFromDom(doc, EPISODE_SELECTORS) ?? parseEpisodeTitleFromPageTitle(titleFallback);
  const seriesTitle = extractTextFromDom(doc, SERIES_SELECTORS) ?? parseSeriesTitleFromPageTitle(titleFallback);
  const seasonTitle = extractTextFromDom(doc, SEASON_SELECTORS) ?? undefined;
  const episodeNumber = extractEpisodeNumber(`${episodeTitle ?? ''} ${parsed.pathname}`);
  const seasonNumber = seasonTitle ? extractSeasonNumber(seasonTitle) : undefined;
  const titleText = buildTitleText(seriesTitle, episodeTitle, titleFallback, parsed.href);

  return {
    siteId: SiteIds.Crunchyroll,
    observedUrl: parsed.href,
    siteMediaId,
    titleText,
    seriesTitle,
    episodeTitle,
    episodeNumber: episodeNumber ?? undefined,
    seasonTitle,
    seasonNumber,
    progressHint: episodeNumber,
    extensionVersion: EXTENSION_VERSION,
  };
}

export function buildCrunchyrollObservation(
  url: string,
  doc: DocumentLike,
): MediaObservation | null {
  const metadata = extractCrunchyrollEpisodeMetadata(doc, url);
  if (!metadata) return null;

  return {
    ...metadata,
    observedAt: new Date().toISOString(),
  };
}

function createMediaObservationFromMetadata(
  metadata: CrunchyrollEpisodeMetadata,
  snapshot: WatchProgressSnapshot,
): MediaObservation {
  return {
    ...metadata,
    watchProgressPercent: snapshot.watchProgressPercent,
    durationSeconds: snapshot.durationSeconds,
    positionSeconds: snapshot.positionSeconds,
    observedAt: new Date().toISOString(),
  };
}

export function trackVideoProgress(
  doc: DocumentLike,
  metadata: CrunchyrollEpisodeMetadata,
  onThresholdReached: (observation: MediaObservation) => void,
  threshold = WATCH_PROGRESS_THRESHOLD,
): VideoProgressTracker | null {
  const video = findActiveVideo(doc);
  if (!video) return null;

  let fired = false;

  const evaluate = () => {
    if (fired) return;

    const snapshot = readWatchProgress(video);
    if (!snapshot) return;

    if (snapshot.watchProgressPercent / 100 >= threshold) {
      fired = true;
      onThresholdReached(createMediaObservationFromMetadata(metadata, snapshot));
    }
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

function parseLocation(locationLike: LocationLike | string): LocationLike | null {
  if (typeof locationLike !== 'string') return locationLike;

  try {
    const url = new URL(locationLike);
    return {
      href: url.href,
      hostname: url.hostname,
      pathname: url.pathname,
    };
  } catch {
    return null;
  }
}

function parseEpisodeTitleFromPageTitle(title: string): string | undefined {
  const parts = splitTitleParts(title);
  return parts.length > 1 ? parts.slice(0, -1).join(' - ') : title || undefined;
}

function parseSeriesTitleFromPageTitle(title: string): string | undefined {
  const parts = splitTitleParts(title);
  return parts.length > 1 ? parts[parts.length - 1] : undefined;
}

function splitTitleParts(title: string): string[] {
  const separator = title.includes(' | ') ? ' | ' : ' - ';
  return title
    .split(separator)
    .map((part) => part.trim())
    .filter(Boolean);
}

function buildTitleText(
  seriesTitle: string | undefined,
  episodeTitle: string | undefined,
  fallback: string,
  url: string,
): string {
  if (seriesTitle && episodeTitle && episodeTitle !== seriesTitle) {
    return `${seriesTitle} - ${episodeTitle}`;
  }

  return episodeTitle || seriesTitle || fallback || url;
}

function findActiveVideo(doc: DocumentLike): VideoElementLike | null {
  const videos = doc.querySelectorAll?.('video')
    .filter((candidate): candidate is VideoElementLike => isVideoElementLike(candidate)) ?? [];

  if (videos.length > 0) {
    return videos.find((video) => !video.paused) ?? videos[0];
  }

  const video = doc.querySelector('video');
  return isVideoElementLike(video) ? video : null;
}

function isVideoElementLike(value: unknown): value is VideoElementLike {
  return Boolean(value)
    && typeof value === 'object'
    && typeof (value as VideoElementLike).currentTime === 'number'
    && typeof (value as VideoElementLike).duration === 'number'
    && typeof (value as VideoElementLike).addEventListener === 'function'
    && typeof (value as VideoElementLike).removeEventListener === 'function';
}

function readWatchProgress(video: VideoElementLike): WatchProgressSnapshot | null {
  if (!Number.isFinite(video.duration) || video.duration <= 0) return null;
  if (!Number.isFinite(video.currentTime) || video.currentTime < 0) return null;

  const positionSeconds = Math.min(video.currentTime, video.duration);
  const watchProgressPercent = Math.min(100, Math.round((positionSeconds / video.duration) * 10000) / 100);

  return {
    watchProgressPercent,
    durationSeconds: Math.round(video.duration * 100) / 100,
    positionSeconds: Math.round(positionSeconds * 100) / 100,
  };
}
