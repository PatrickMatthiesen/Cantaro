import { z } from 'zod';
import type {
  EpisodeStreamingDestinations,
  MediaStreamingDestinations,
  StreamingDestination,
} from './streamingDestinations';

const stremioTargetSchema = z.object({
  type: z.enum(['movie', 'series']),
  id: z.string().regex(/^(?:tt\d+|kitsu:[1-9]\d*)$/),
  // Invalid episode evidence must not remove a valid title fallback.
  episodeMapping: z.object({
    seasonNumber: z.number().int().positive().nullable(),
    episodeOffset: z.number().int().nonnegative(),
  }).nullish().catch(undefined),
});

const stremioRouteBuilders = {
  movie: (id: string) => `stremio:///detail/movie/${id}/${id}`,
  series: (id: string) => `stremio:///detail/series/${id}`,
} as const;

const supportedMediaKinds = new Set(['anime', 'movie', 'series']);

/**
 * Builds a title-level Stremio deep link from a provider-validated target.
 * Runtime validation keeps untrusted response data out of the custom URL scheme.
 */
export function buildStremioDetailUrl(target: unknown): string | null {
  const parsed = stremioTargetSchema.safeParse(target);
  if (!parsed.success) return null;

  return stremioRouteBuilders[parsed.data.type](parsed.data.id);
}

export function findStremioDetailUrl(targets: readonly unknown[]): string | null {
  for (const target of targets) {
    const url = buildStremioDetailUrl(target);
    if (url) return url;
  }

  return null;
}

/**
 * Appends provider-validated Stremio routes after the normal HTTPS destination
 * pipeline. Stremio remains excluded from imported external-link validation.
 */
export function addStremioDestinations(
  destinations: MediaStreamingDestinations,
  targets: readonly unknown[],
  mediaKind: string,
  episodeNumbers: readonly number[] = [],
): MediaStreamingDestinations {
  const kind = mediaKind.trim().toLowerCase();
  if (!supportedMediaKinds.has(kind)) return destinations;

  const candidates = parseTargets(targets);
  const target = candidates[0];
  if (!target) return destinations;

  const episodes = destinations.episodes.map(episode =>
    addEpisodeDestination(episode, candidates, kind));
  const missingEpisodes = createMissingEpisodes(episodes, episodeNumbers)
    .map(episode => addEpisodeDestination(episode, candidates, kind))
    .filter(episode => episode.destinations.length > 0);

  return {
    ...destinations,
    seriesDestinations: appendStremioDestination(
      destinations.seriesDestinations,
      stremioDestination(stremioRouteBuilders[target.type](target.id), 'series'),
    ),
    episodes: [...episodes, ...missingEpisodes].sort((left, right) => left.episodeNumber - right.episodeNumber),
  };
}

type StremioTarget = z.infer<typeof stremioTargetSchema>;

function parseTargets(targets: readonly unknown[]): StremioTarget[] {
  return targets.flatMap(target => {
    const parsed = stremioTargetSchema.safeParse(target);
    return parsed.success ? [parsed.data] : [];
  });
}

export function addAioStreamsDestinations(
  destinations: MediaStreamingDestinations,
  instanceUrl: string | undefined,
  targets: readonly unknown[],
  mediaKind: string,
  catalogEpisodeNumbers: readonly number[],
): MediaStreamingDestinations {
  const base = parseAioStreamsInstance(instanceUrl);
  if (!base || !supportedMediaKinds.has(mediaKind)) return destinations;

  const candidates = parseTargets(targets);
  if (mediaKind === 'movie' || mediaKind === 'anime' && candidates.some(target => target.type === 'movie')) {
    return withAioStreamsMovie(destinations, base, candidates);
  }

  const episodes = addAioStreamsEpisodes(destinations.episodes, catalogEpisodeNumbers, base, candidates, mediaKind);
  return { ...destinations, episodes };
}

function isAioStreamsEpisodeTarget(target: StremioTarget, type: 'anime' | 'series'): boolean {
  if (target.type !== 'series') return false;
  if (type === 'series') return isImdbId(target.id);

  // IMDb title offsets omit anime episode gaps. Kitsu uses entry-relative numbering.
  return target.id.startsWith('kitsu:')
    && target.episodeMapping?.seasonNumber === null
    && target.episodeMapping.episodeOffset === 0;
}

function withAioStreamsMovie(
  destinations: MediaStreamingDestinations,
  base: URL,
  candidates: StremioTarget[],
): MediaStreamingDestinations {
  const ids = new Set(candidates.filter(target => target.type === 'movie' && isImdbId(target.id)).map(target => target.id));
  if (ids.size !== 1) return destinations;
  return {
    ...destinations,
    seriesDestinations: [...destinations.seriesDestinations, aioStreamsDestination(base, 'movie', [...ids][0]!)],
  };
}

function addAioStreamsEpisodes(
  episodes: EpisodeStreamingDestinations[],
  catalogEpisodeNumbers: readonly number[],
  base: URL,
  candidates: StremioTarget[],
  mediaKind: string,
): EpisodeStreamingDestinations[] {
  const type = mediaKind === 'anime' ? 'anime' : 'series';
  const episodeTargets = candidates.filter(target => isAioStreamsEpisodeTarget(target, type));
  const ids = new Set(episodeTargets.map(target => target.id));
  if (ids.size !== 1) return episodes;

  const metaId = [...ids][0]!;
  const catalogNumbers = new Set(catalogEpisodeNumbers);
  // One provider video must not open from two different canonical episode rows.
  const urlCounts = new Map<string, number>();
  const routes = episodes.map(episode => {
    if (!catalogNumbers.has(episode.episodeNumber) || !isPositiveInteger(episode.episodeNumber)) return;
    const videoId = type === 'anime'
      ? `${metaId}:${episode.episodeNumber}`
      : buildImdbEpisodeVideoId(metaId, episode.seasonNumber, episode.seasonEpisodeNumber);
    if (!videoId) return;

    const route = aioStreamsDestination(base, type, metaId, videoId);
    urlCounts.set(route.url, (urlCounts.get(route.url) ?? 0) + 1);
    return route;
  });
  return episodes.map((episode, index) => {
    const route = routes[index];
    return route && urlCounts.get(route.url) === 1
      ? { ...episode, destinations: [...episode.destinations, route] }
      : episode;
  }).sort((left, right) => left.episodeNumber - right.episodeNumber);
}

function parseAioStreamsInstance(value: string | undefined): URL | null {
  if (!value || /[?#\\]/.test(value)) return null;
  try {
    const url = new URL(value);
    return isAioStreamsInstance(url) ? new URL('jellyfin/web/', `${url.href.replace(/\/$/, '')}/`) : null;
  } catch {
    return null;
  }
}

function isAioStreamsInstance(url: URL): boolean {
  if (url.protocol !== 'https:' || url.username || url.password) return false;

  const path = `/${decodeURIComponent(url.pathname).split('/').filter(Boolean).join('/')}/`;
  const accountPath = /\/(?:stremio|chilllink|seanime)\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\//i;
  const installPath = /\/(?:jellyfin|manifest\.json|seanime\/extensions|stremio\/u\/[^/]+)\//i;
  return !accountPath.test(path) && !installPath.test(path);
}

function aioStreamsDestination(
  base: URL,
  type: 'anime' | 'series' | 'movie',
  metaId: string,
  videoId?: string,
): StreamingDestination {
  const params = new URLSearchParams({ type, metaId });
  if (videoId) params.set('videoId', videoId);
  return {
    serviceId: 'aiostreams',
    displayName: 'AIOStreams',
    kind: videoId ? 'episode' : 'series',
    url: `${base.href}#/open?${params}`,
  };
}

function createMissingEpisodes(
  episodes: EpisodeStreamingDestinations[],
  episodeNumbers: readonly number[],
): EpisodeStreamingDestinations[] {
  const existingNumbers = new Set(episodes.map(episode => episode.episodeNumber));
  return [...new Set(episodeNumbers)]
    .filter(number => isPositiveInteger(number) && !existingNumbers.has(number))
    .map(episodeNumber => ({
      episodeNumber,
      availableAudioLanguageCodes: [],
      availableSubtitleLanguageCodes: [],
      destinations: [],
    }));
}

function isImdbId(id: string): boolean {
  return /^tt\d+$/.test(id);
}

function buildImdbEpisodeVideoId(
  id: string,
  seasonNumber: number | undefined,
  seasonEpisodeNumber: number | undefined,
): string | null {
  if (!isPositiveInteger(seasonNumber) || !isPositiveInteger(seasonEpisodeNumber)) {
    return null;
  }

  return `${id}:${seasonNumber}:${seasonEpisodeNumber}`;
}

function buildEpisodeVideoId(
  target: StremioTarget,
  episode: EpisodeStreamingDestinations,
  mediaKind: string,
): string | null {
  if (target.type !== 'series' || !isPositiveInteger(episode.episodeNumber)) return null;
  if (target.episodeMapping) {
    return buildMappedEpisodeVideoId(target.id, target.episodeMapping, episode.episodeNumber);
  }

  // Anime season numbering may come from TVDB or another provider, not IMDb.
  return mediaKind === 'series' && isImdbId(target.id)
    ? buildImdbEpisodeVideoId(target.id, episode.seasonNumber, episode.seasonEpisodeNumber)
    : null;
}

function buildMappedEpisodeVideoId(
  id: string,
  mapping: NonNullable<StremioTarget['episodeMapping']>,
  episodeNumber: number,
): string | null {
  const number = episodeNumber + mapping.episodeOffset;
  if (!isPositiveInteger(number)) return null;
  if (isImdbId(id)) {
    return buildImdbEpisodeVideoId(id, mapping.seasonNumber ?? undefined, number);
  }
  return mapping.seasonNumber === null
    ? `${id}:${number}` : null;
}

function addEpisodeDestination(
  episode: EpisodeStreamingDestinations,
  candidates: StremioTarget[],
  mediaKind: string,
): EpisodeStreamingDestinations {
  let url: string | null = null;
  for (const target of candidates) {
    const videoId = buildEpisodeVideoId(target, episode, mediaKind);
    if (videoId) {
      url = `stremio:///detail/series/${target.id}/${videoId}`;
      break;
    }
  }
  return url
    ? {
        ...episode,
        destinations: appendStremioDestination(
          episode.destinations,
          stremioDestination(url, 'episode'),
        ),
      }
    : episode;
}

function isPositiveInteger(value: number | undefined): value is number {
  return Number.isSafeInteger(value) && (value ?? 0) > 0;
}

function stremioDestination(
  url: string,
  kind: StreamingDestination['kind'],
): StreamingDestination {
  return {
    serviceId: 'stremio',
    url,
    kind,
    displayName: 'Stremio',
  };
}

function appendStremioDestination(
  destinations: StreamingDestination[],
  stremio: StreamingDestination,
): StreamingDestination[] {
  return destinations.some(destination => destination.serviceId === 'stremio')
    ? destinations
    : [...destinations, stremio];
}
