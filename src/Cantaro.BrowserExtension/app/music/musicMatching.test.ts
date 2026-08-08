import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { describe, expect, it } from 'vitest';
import type { MusicTabContext } from '../../features/music/contracts/musicTabContext';
import { resolveMusicTab } from './musicMatching';

function context(overrides: Partial<MusicTabContext> = {}): MusicTabContext {
  return {
    feature: 'music',
    provider: 'youtube',
    pageKind: 'track',
    pageUrl: 'https://youtube.com/watch?v=video-1',
    status: 'ready',
    externalId: 'video-1',
    title: 'A Song (Official Audio)',
    artist: 'The Artist',
    ...overrides,
  };
}

function song(id: string, externalId: string): MusicLibrarySong {
  return {
    id,
    title: 'A Song',
    artist: 'The Artist',
    artistCredits: [],
    albums: [],
    playlists: [],
    sourcePlatforms: ['youtube'],
    sourceIdentities: [{ source: 'youtube', externalId }],
    platformLinks: [],
  };
}

describe('resolveMusicTab', () => {
  it('prefers one exact source identity', () => {
    expect(resolveMusicTab(context(), [song('one', 'video-1')])).toMatchObject({ status: 'matched' });
  });

  it('does not choose between duplicate source identities', () => {
    expect(resolveMusicTab(context(), [song('one', 'video-1'), song('two', 'video-1')])).toMatchObject({ status: 'ambiguous' });
  });

  it('falls back to normalized title and artist metadata', () => {
    expect(resolveMusicTab(context({ externalId: 'unknown' }), [song('one', 'different')])).toMatchObject({ status: 'matched' });
  });
});
