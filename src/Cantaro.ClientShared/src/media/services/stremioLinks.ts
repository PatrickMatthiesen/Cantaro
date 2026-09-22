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

  const candidates = targets.flatMap(target => {
    const parsed = stremioTargetSchema.safeParse(target);
    return parsed.success ? [parsed.data] : [];
  });
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

function buildEpisodeUrl(
  target: StremioTarget,
  episode: EpisodeStreamingDestinations,
  mediaKind: string,
): string | null {
  if (target.type !== 'series' || !isPositiveInteger(episode.episodeNumber)) return null;
  if (target.episodeMapping) {
    return buildMappedEpisodeUrl(target.id, target.episodeMapping, episode.episodeNumber);
  }

  // Anime season numbering may come from TVDB or another provider, not IMDb.
  return mediaKind === 'series' && isImdbId(target.id)
    ? buildImdbEpisodeUrl(target.id, episode.seasonNumber, episode.seasonEpisodeNumber)
    : null;
}

function buildMappedEpisodeUrl(
  id: string,
  mapping: NonNullable<StremioTarget['episodeMapping']>,
  episodeNumber: number,
): string | null {
  const number = episodeNumber + mapping.episodeOffset;
  if (!isPositiveInteger(number)) return null;
  if (isImdbId(id)) {
    return buildImdbEpisodeUrl(id, mapping.seasonNumber ?? undefined, number);
  }
  return mapping.seasonNumber === null
    ? `stremio:///detail/series/${id}/${id}:${number}` : null;
}

function addEpisodeDestination(
  episode: EpisodeStreamingDestinations,
  candidates: StremioTarget[],
  mediaKind: string,
): EpisodeStreamingDestinations {
  const url = candidates.map(target => buildEpisodeUrl(target, episode, mediaKind))
    .find(candidate => candidate !== null);
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
