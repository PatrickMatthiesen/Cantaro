import type { ComponentType, SVGProps } from 'react';
import AppleTv from '@thesvg/react/apple-tv';
import Crunchyroll from '@thesvg/react/crunchyroll';
import DisneyPlus from '@thesvg/react/disney-plus';
import Hulu from '@thesvg/react/hulu';
import Max from '@thesvg/react/max';
import Netflix from '@thesvg/react/netflix';
import ParamountPlus from '@thesvg/react/paramountplus';
import Peacock from '@thesvg/react/peacock';
import PrimeVideo from '@thesvg/react/prime-video';
import YouTube from '@thesvg/react/youtube';

export const STREAMING_SERVICE_IDS = [
  'crunchyroll',
  'hidive',
  'netflix',
  'hulu',
  'disney-plus',
  'prime-video',
  'max',
  'apple-tv',
  'paramount-plus',
  'peacock',
  'youtube',
] as const;

export type StreamingServiceId = typeof STREAMING_SERVICE_IDS[number];
export type StreamingServiceIcon = ComponentType<SVGProps<SVGSVGElement>>;

export interface StreamingServiceCapabilities {
  seriesDestinations: boolean;
  episodeDestinations: boolean;
}

export interface StreamingServiceDefinition {
  id: StreamingServiceId;
  displayName: string;
  allowedHosts: readonly string[];
  brandColor: string;
  buttonColor: string;
  icon?: StreamingServiceIcon;
  capabilities: StreamingServiceCapabilities;
  seriesPathPatterns: readonly RegExp[];
  episodePathPatterns: readonly RegExp[];
  searchUrl?: (query: string) => string;
}

const direct = { seriesDestinations: true, episodeDestinations: true } as const;
const seriesOnly = { seriesDestinations: true, episodeDestinations: false } as const;

export const STREAMING_SERVICES: Readonly<Record<StreamingServiceId, StreamingServiceDefinition>> = {
  crunchyroll: service(
    'crunchyroll',
    'Crunchyroll',
    ['crunchyroll.com'],
    '#ff640a',
    '#c94f08',
    direct,
    Crunchyroll,
    [/^\/series\/[a-z0-9]+(?:\/|$)/i],
    [/^\/watch\/[a-z0-9]+(?:\/|$)/i],
    crunchyrollSearchUrl,
  ),
  hidive: service('hidive', 'HIDIVE', ['hidive.com'], '#00a4dc', '#00526e', seriesOnly, undefined, [/^\/(?:season|tv)\//i]),
  netflix: service('netflix', 'Netflix', ['netflix.com'], '#ff2137', '#e81e32', seriesOnly, Netflix, [/^\/(?:[a-z]{2}\/)?title\/\d+/i]),
  hulu: service('hulu', 'Hulu', ['hulu.com'], '#1ce783', '#0e7442', seriesOnly, Hulu, [/^\/(?:series|movie)\//i]),
  'disney-plus': service('disney-plus', 'Disney+', ['disneyplus.com'], '#113ccf', '#091e68', seriesOnly, DisneyPlus, [/^\/(?:series|movies|browse\/entity-)/i]),
  'prime-video': service('prime-video', 'Prime Video', ['primevideo.com', 'amazon.com'], '#00a8e1', '#005471', seriesOnly, PrimeVideo, [/^\/(?:detail|gp\/video\/detail)\//i]),
  max: service('max', 'Max', ['max.com'], '#002be7', '#001674', seriesOnly, Max, [/^\/(?:[a-z]{2}\/[a-z]{2}\/)?(?:shows|movies)\//i]),
  'apple-tv': service('apple-tv', 'Apple TV', ['tv.apple.com'], '#111111', '#111111', seriesOnly, AppleTv, [/^\/(?:[a-z]{2}\/)?(?:show|movie)\//i]),
  'paramount-plus': service('paramount-plus', 'Paramount+', ['paramountplus.com'], '#0064ff', '#003280', seriesOnly, ParamountPlus, [/^\/(?:shows|movies)\//i]),
  peacock: service('peacock', 'Peacock', ['peacocktv.com'], '#f5e500', '#7b7300', seriesOnly, Peacock, [/^\/(?:watch\/asset|collections)\//i]),
  youtube: service('youtube', 'YouTube', ['youtube.com', 'youtu.be'], '#ff0033', '#ed002f', seriesOnly, YouTube, [/^\/(?:playlist|channel|@)/i]),
};

const SERVICE_ALIASES: Readonly<Record<string, StreamingServiceId>> = {
  crunchyroll: 'crunchyroll',
  hidive: 'hidive',
  netflix: 'netflix',
  hulu: 'hulu',
  disney: 'disney-plus',
  'disney+': 'disney-plus',
  disneyplus: 'disney-plus',
  'disney-plus': 'disney-plus',
  amazon: 'prime-video',
  'amazon-prime': 'prime-video',
  prime: 'prime-video',
  'prime-video': 'prime-video',
  max: 'max',
  'hbo-max': 'max',
  'apple-tv': 'apple-tv',
  appletv: 'apple-tv',
  paramount: 'paramount-plus',
  'paramount+': 'paramount-plus',
  paramountplus: 'paramount-plus',
  'paramount-plus': 'paramount-plus',
  peacock: 'peacock',
  youtube: 'youtube',
};

export function resolveStreamingServiceId(value: string): StreamingServiceId | null {
  return SERVICE_ALIASES[normalizeIdentifier(value)] ?? null;
}

export function isStreamingServiceId(value: unknown): value is StreamingServiceId {
  return typeof value === 'string' && STREAMING_SERVICE_IDS.includes(value as StreamingServiceId);
}

export function isAllowedStreamingUrl(serviceId: StreamingServiceId, value: string): boolean {
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:') return false;
    const hostname = url.hostname.toLowerCase();
    return STREAMING_SERVICES[serviceId].allowedHosts.some(
      allowed => hostname === allowed || hostname.endsWith(`.${allowed}`),
    );
  } catch {
    return false;
  }
}

export function isStreamingDestinationUrl(
  serviceId: StreamingServiceId,
  value: string,
  kind: 'series' | 'episode',
): boolean {
  if (!isAllowedStreamingUrl(serviceId, value)) return false;
  const patterns = kind === 'series'
    ? STREAMING_SERVICES[serviceId].seriesPathPatterns
    : STREAMING_SERVICES[serviceId].episodePathPatterns;
  return patterns.some(pattern => pattern.test(new URL(value).pathname));
}

function normalizeIdentifier(value: string): string {
  return value.trim().toLowerCase().replace(/[\s_]+/g, '-');
}

function service(
  id: StreamingServiceId,
  displayName: string,
  allowedHosts: readonly string[],
  brandColor: string,
  buttonColor: string,
  capabilities: StreamingServiceCapabilities,
  icon?: StreamingServiceIcon,
  seriesPathPatterns: readonly RegExp[] = [],
  episodePathPatterns: readonly RegExp[] = [],
  searchUrl?: (query: string) => string,
): StreamingServiceDefinition {
  return {
    id,
    displayName,
    allowedHosts,
    brandColor,
    buttonColor,
    capabilities,
    icon,
    seriesPathPatterns,
    episodePathPatterns,
    searchUrl,
  };
}

function crunchyrollSearchUrl(title: string): string {
  const query = Array.from(title.trim()).slice(0, 32).join('').trimEnd();
  return `https://www.crunchyroll.com/search?q=${encodeURIComponent(query)}`;
}
