import type { CrunchyrollWatchMetadata } from './watchAdapter';

export function watchMetadataFingerprint(metadata: CrunchyrollWatchMetadata): string {
  return JSON.stringify([
    metadata.seriesTitle,
    metadata.episodeTitle,
    metadata.episodeNumber,
    metadata.seasonTitle,
    metadata.seasonNumber,
  ]);
}

export function isFreshNavigationMetadata(
  previousFingerprint: string | undefined,
  metadata: CrunchyrollWatchMetadata,
): boolean {
  if (!previousFingerprint) return true;
  return watchMetadataFingerprint(metadata) !== previousFingerprint;
}
