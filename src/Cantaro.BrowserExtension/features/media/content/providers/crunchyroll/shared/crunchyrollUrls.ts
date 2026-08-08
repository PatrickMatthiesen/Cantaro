const WATCH_ID_RE = /\/watch\/([A-Z0-9]+)(?:\/|$)/i;
const SERIES_ID_RE = /\/series\/([A-Z0-9]+)(?:\/|$)/i;
const EPISODE_NUMBER_RE = /\b(?:episode|ep|e)[- _]?(\d+)(?![\d.])/i;
const SEASON_NUMBER_RE = /season\s*(\d+)/i;

export function extractEpisodeId(pathname: string): string | undefined {
  return WATCH_ID_RE.exec(pathname)?.[1];
}

export function extractSeriesId(pathname: string): string | undefined {
  return SERIES_ID_RE.exec(pathname)?.[1];
}

export function extractEpisodeNumber(text: string): number | null {
  const value = EPISODE_NUMBER_RE.exec(text)?.[1];
  return value ? Number.parseInt(value, 10) : null;
}

export function extractSeasonNumber(text: string): number | undefined {
  const value = SEASON_NUMBER_RE.exec(text)?.[1];
  return value ? Number.parseInt(value, 10) : undefined;
}

function isCrunchyrollHost(hostname: string): boolean {
  const normalized = hostname.toLowerCase();
  return normalized === 'crunchyroll.com' || normalized === 'www.crunchyroll.com';
}

export function parseCrunchyrollUrl(value: string, baseUrl?: string): URL | null {
  try {
    const url = baseUrl ? new URL(value, baseUrl) : new URL(value);
    if (url.protocol !== 'https:') return null;
    return isCrunchyrollHost(url.hostname) ? url : null;
  } catch {
    return null;
  }
}
