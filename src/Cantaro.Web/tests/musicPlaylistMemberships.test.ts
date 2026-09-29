import { expect, test } from 'bun:test';
import type { MusicLibraryPlaylist, MusicLibrarySongPlaylist } from '../../Cantaro.ClientShared/src/music/services/musicLibraryApi';
import { canAddSongToPlaylist, withoutPlaylistEntry } from '../src/music/musicPlaylistMemberships';

const playlist = { id: 'playlist', allowDuplicateTracks: false } as MusicLibraryPlaylist;
const memberships: MusicLibrarySongPlaylist[] = [
  { entryId: 'first', playlistId: 'playlist', playlistName: 'Songs', position: 1 },
  { entryId: 'second', playlistId: 'playlist', playlistName: 'Songs', position: 2 },
];

test('allows another occurrence only when playlist duplicates are enabled', () => {
  expect(canAddSongToPlaylist(playlist, memberships)).toBe(false);
  expect(canAddSongToPlaylist({ ...playlist, allowDuplicateTracks: true }, memberships)).toBe(true);
});

test('removes one selected occurrence and keeps the other', () => {
  expect(withoutPlaylistEntry(memberships, 'second')).toEqual([memberships[0]]);
});
