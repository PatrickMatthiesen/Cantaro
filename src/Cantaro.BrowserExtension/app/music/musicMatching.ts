import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import type { MusicTabContext } from '../../features/music/contracts/musicTabContext';

export type MusicContextResolution =
  | { status: 'matched'; song: MusicLibrarySong }
  | { status: 'ambiguous'; songs: MusicLibrarySong[] }
  | { status: 'unmatched' };

function normalizedMusicLabel(value?: string) {
  return (value ?? '')
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\([^)]*(official|audio|video|lyrics)[^)]*\)/gi, '')
    .replace(/[^a-z0-9]+/gi, ' ')
    .trim()
    .toLowerCase();
}

export function resolveMusicTab(context: MusicTabContext, songs: MusicLibrarySong[]): MusicContextResolution {
  const idMatches = songs.filter((song) => song.sourceIdentities.some((identity) =>
    identity.source.toLowerCase().includes('youtube') && identity.externalId === context.externalId));
  if (idMatches.length === 1) return { status: 'matched', song: idMatches[0] };
  if (idMatches.length > 1) return { status: 'ambiguous', songs: idMatches };

  const title = normalizedMusicLabel(context.title);
  if (!title) return { status: 'unmatched' };
  const metadataMatches = songs.filter((song) => normalizedMusicLabel(song.title) === title
    && (!context.artist || normalizedMusicLabel(song.artist) === normalizedMusicLabel(context.artist)));
  if (metadataMatches.length === 1) return { status: 'matched', song: metadataMatches[0] };
  if (metadataMatches.length > 1) return { status: 'ambiguous', songs: metadataMatches };
  return { status: 'unmatched' };
}
