import { expect, test } from 'bun:test';
import { playlistMatchSearchQuery } from '../src/music/playlistMatchSearchQuery';

test('an unchanged automatic suggestion uses the backend automatic search', () => {
  expect(playlistMatchSearchQuery('  Mortals · LXNGVX, Warriyo  ', 'Mortals · LXNGVX, Warriyo')).toBeUndefined();
});

test('an edited query is sent explicitly', () => {
  expect(playlistMatchSearchQuery('Mortals MrMoMMusic', 'Mortals · LXNGVX, Warriyo')).toBe('Mortals MrMoMMusic');
});

test('blank search input is not sent', () => {
  expect(playlistMatchSearchQuery('   ', 'Mortals · LXNGVX, Warriyo')).toBeUndefined();
});
