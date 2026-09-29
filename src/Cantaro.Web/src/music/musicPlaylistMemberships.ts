import type { MusicLibraryPlaylist, MusicLibrarySongPlaylist } from '@cantaro/client-shared/music';

export function canAddSongToPlaylist(playlist: MusicLibraryPlaylist, memberships: MusicLibrarySongPlaylist[]) {
  return playlist.allowDuplicateTracks || !memberships.some((membership) => membership.playlistId === playlist.id);
}

export function withoutPlaylistEntry(memberships: MusicLibrarySongPlaylist[], entryId: string) {
  return memberships.filter((membership) => membership.entryId !== entryId);
}
