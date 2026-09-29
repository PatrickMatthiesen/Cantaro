import type { PlatformPlaylist } from '@cantaro/client-shared/music';

export type PlaylistAttachCandidate = { playlist: PlatformPlaylist; ownerId: string | null };

function normalizedName(name: string) {
  return name.normalize('NFKC').trim().replace(/\s+/g, ' ').toLocaleLowerCase();
}

export function playlistAttachCandidates(candidates: PlaylistAttachCandidate[], playlistName: string, query: string) {
  const name = normalizedName(playlistName);
  const search = normalizedName(query);
  return candidates
    .map((candidate) => ({ ...candidate, sameName: !!name && normalizedName(candidate.playlist.title) === name }))
    .filter(({ playlist }) => normalizedName(playlist.title).includes(search))
    .sort((a, b) => Number(b.sameName) - Number(a.sameName)
      || Number(!!a.ownerId) - Number(!!b.ownerId)
      || a.playlist.title.localeCompare(b.playlist.title, undefined, { numeric: true, sensitivity: 'base' }));
}
