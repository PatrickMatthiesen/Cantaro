import { z } from 'zod';
import type {
  EpisodeStreamingDestinations,
  MediaStreamingDestinations,
  StreamingDestination,
} from './streamingDestinations';

const stremioTargetSchema = z.object({
  type: z.enum(['movie', 'series']),
  id: z.string().regex(/^(?:tt\d+|kitsu:[1-9]\d*)$/),
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
): MediaStreamingDestinations {
  if (!supportedMediaKinds.has(mediaKind.trim().toLowerCase())) return destinations;

  const target = findStremioTarget(targets);
  if (!target) return destinations;

  return {
    ...destinations,
    seriesDestinations: appendStremioDestination(
      destinations.seriesDestinations,
      stremioDestination(stremioRouteBuilders[target.type](target.id), 'series'),
    ),
    episodes: target.type === 'series' && isImdbId(target.id)
      ? destinations.episodes.map(episode => addImdbEpisodeDestination(episode, target.id))
      : destinations.episodes,
  };
}

type StremioTarget = z.infer<typeof stremioTargetSchema>;

function findStremioTarget(targets: readonly unknown[]): StremioTarget | null {
  for (const target of targets) {
    const parsed = stremioTargetSchema.safeParse(target);
    if (parsed.success) return parsed.data;
  }

  return null;
}

function isImdbId(id: string): boolean {
  return /^tt\d+$/.test(id);
}

function buildImdbEpisodeUrl(
  id: string,
  seasonNumber: number | undefined,
  seasonEpisodeNumber: number | undefined,
): string | null {
  if (!isPositiveInteger(seasonNumber) || !isPositiveInteger(seasonEpisodeNumber)) {
    return null;
  }

  return `stremio:///detail/series/${id}/${id}:${seasonNumber}:${seasonEpisodeNumber}`;
}

function addImdbEpisodeDestination(
  episode: EpisodeStreamingDestinations,
  id: string,
): EpisodeStreamingDestinations {
  const url = buildImdbEpisodeUrl(
    id,
    episode.seasonNumber,
    episode.seasonEpisodeNumber,
  );
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
  return Number.isInteger(value) && (value ?? 0) > 0;
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
