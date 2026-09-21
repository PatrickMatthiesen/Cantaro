import { z } from 'zod';

const stremioTargetSchema = z.object({
  type: z.enum(['movie', 'series']),
  id: z.string().regex(/^(?:tt\d+|kitsu:[1-9]\d*)$/),
});

const stremioRouteBuilders = {
  movie: (id: string) => `stremio:///detail/movie/${id}/${id}`,
  series: (id: string) => `stremio:///detail/series/${id}`,
} as const;

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
