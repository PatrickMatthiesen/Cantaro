import { expect, test } from 'bun:test';
import { playlistAttachCandidates } from '../src/music/playlistAttachCandidates';

function candidate(id: string, title: string, ownerId: string | null = null) {
  return { playlist: { id, title, itemCount: 12, songs: async () => [] }, ownerId };
}

test('same names sort first, available before linked, then alphabetically', () => {
  const input = [candidate('linked', 'Coding', 'another'), candidate('z', 'Zoo'),
    candidate('match', '  DOWNLOAD til   iphone '), candidate('a', 'Alpha'),
    candidate('owned-match', 'download til iphone', 'owner')];
  const result = playlistAttachCandidates(input, 'download til iphone', '');
  expect(result.map(item => item.playlist.id)).toEqual(['match', 'owned-match', 'a', 'z', 'linked']);
  expect(result[0].sameName).toBe(true);
  expect(result[1].ownerId).toBe('owner');
  expect(input[0].playlist.id).toBe('linked');
});

test('search preserves accents and same-name priority without selecting or merging', () => {
  const result = playlistAttachCandidates([candidate('more', 'Løb 2'), candidate('exact', 'Løb'), candidate('other', 'Gym')], 'Løb', 'LØB');
  expect(result.map(item => item.playlist.id)).toEqual(['exact', 'more']);
  expect(result.map(item => item.sameName)).toEqual([true, false]);
});
