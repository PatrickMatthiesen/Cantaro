import { describe, expect, test } from 'bun:test';
import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import {
  currentPlaybackTrack,
  initialPlaybackState,
  isPlayerVisible,
  playbackAvailability,
  playbackReducer,
  selectedPlaybackSource,
  type PlaybackAction,
  type PlaybackTrack,
} from '../src/music/playback/playbackState';
import { playbackTrackFromSong } from '../src/music/playback/playbackSources';

const youtube: PlaybackTrack = {
  id: 'first', title: 'First', sources: [{
    id: 'youtube:one', providerId: 'youtube', label: 'YouTube',
    capability: { kind: 'embed', mediaId: 'one', externalUrl: 'https://www.youtube.com/watch?v=one' },
  }],
};
const spotify: PlaybackTrack = {
  id: 'second', title: 'Second', sources: [{
    id: 'spotify:two', providerId: 'spotify', label: 'Spotify',
    capability: { kind: 'external', url: 'https://open.spotify.com/track/two', reason: 'Open in Spotify.' },
  }],
};
const unavailable: PlaybackTrack = {
  id: 'third', title: 'Third', sources: [{
    id: 'other:three', providerId: 'other', label: 'Other',
    capability: { kind: 'unavailable', reason: 'This provider cannot play here.', externalUrl: 'https://other.example/three' },
  }],
};

function apply(...actions: PlaybackAction[]) {
  return actions.reduce(playbackReducer, initialPlaybackState);
}

describe('playback session', () => {
  test('queueing does not reveal the player; selection does', () => {
    const queued = apply({ type: 'enqueue', track: youtube });
    expect(queued.currentIndex).toBeNull();
    expect(isPlayerVisible(queued)).toBe(false);
    expect(playbackReducer(queued, { type: 'play' })).toEqual(queued);

    const selected = playbackReducer(queued, { type: 'select', index: 0 });
    expect(isPlayerVisible(selected)).toBe(true);
    expect(selected.status).toBe('paused');
    expect(currentPlaybackTrack(selected)?.id).toBe('first');
    expect(playbackReducer(selected, { type: 'play' }).status).toBe('playing');
  });

  test('next and previous retain play intent only for embeddable sources', () => {
    const started = apply(
      { type: 'replaceQueue', tracks: [youtube, spotify, unavailable, youtube] },
      { type: 'select', index: 0, play: true },
    );
    expect(started.status).toBe('playing');
    const external = playbackReducer(started, { type: 'next' });
    expect(external.currentIndex).toBe(1);
    expect(external.status).toBe('paused');
    expect(playbackAvailability(external)).toEqual({
      canPlayInApp: false, reason: 'Open in Spotify.',
      externalUrl: 'https://open.spotify.com/track/two', externalLabel: 'Spotify',
    });
    expect(playbackReducer(external, { type: 'play' })).toEqual(external);

    const missing = playbackReducer(external, { type: 'next' });
    expect(missing.currentIndex).toBe(2);
    expect(playbackAvailability(missing).reason).toBe('This provider cannot play here.');
    expect(playbackAvailability(missing).externalUrl).toBe('https://other.example/three');
    expect(playbackReducer(missing, { type: 'previous' }).currentIndex).toBe(1);

    const last = playbackReducer(missing, { type: 'next' });
    expect(last.currentIndex).toBe(3);
    expect(last.status).toBe('paused');
    expect(playbackReducer(last, { type: 'next' })).toEqual(last);
    expect(playbackReducer(started, { type: 'previous' })).toEqual(started);
  });

  test('pause, stop, source selection, and queue replacement are deterministic', () => {
    const both: PlaybackTrack = { ...youtube, sources: [...youtube.sources, ...spotify.sources] };
    const playing = apply(
      { type: 'replaceQueue', tracks: [both] },
      { type: 'select', index: 0, play: true },
    );
    expect(selectedPlaybackSource(playing)?.id).toBe('youtube:one');
    expect(playbackReducer(playing, { type: 'selectSource', sourceId: 'missing' })).toEqual(playing);
    const external = playbackReducer(playing, { type: 'selectSource', sourceId: 'spotify:two' });
    expect(external.status).toBe('paused');
    expect(playbackAvailability(external).canPlayInApp).toBe(false);
    const embedded = playbackReducer(external, { type: 'selectSource', sourceId: 'youtube:one' });
    const paused = playbackReducer(playbackReducer(embedded, { type: 'play' }), { type: 'pause' });
    expect(paused.status).toBe('paused');
    expect(playbackReducer(paused, { type: 'stop' }).status).toBe('stopped');
    expect(isPlayerVisible(paused)).toBe(true);
    expect(playbackReducer(paused, { type: 'replaceQueue', tracks: [spotify] }).currentIndex).toBeNull();
    expect(playbackReducer(paused, { type: 'clearQueue' })).toEqual(initialPlaybackState);
    expect(playbackReducer(paused, { type: 'select', index: -1 })).toEqual(paused);
  });
});

function song(overrides: Partial<MusicLibrarySong>): MusicLibrarySong {
  return {
    id: 'song-1', title: 'A song', artist: 'An artist', artistCredits: [], albums: [],
    sourcePlatforms: [], sourceIdentities: [], platformLinks: [], playlists: [], ...overrides,
  };
}

describe('canonical playback sources', () => {
  test('YouTube embeds while Spotify remains an external fallback', () => {
    const track = playbackTrackFromSong(song({
      sourceIdentities: [
        { source: 'spotify', externalId: 'sp1' },
        { source: 'youtube', externalId: 'yt1' },
      ],
      platformLinks: [
        { platform: 'spotify', label: 'Listen on Spotify', url: 'https://open.spotify.com/track/sp1' },
        { platform: 'youtube', label: 'Watch on YouTube', url: 'https://www.youtube.com/watch?v=yt1' },
      ],
    }));
    const selected = apply({ type: 'replaceQueue', tracks: [track] }, { type: 'select', index: 0, play: true });
    expect(selectedPlaybackSource(selected)?.capability).toEqual({
      kind: 'embed', mediaId: 'yt1', externalUrl: 'https://www.youtube.com/watch?v=yt1',
    });
    expect(track.sources).toHaveLength(2);
    expect(selected.status).toBe('playing');
  });

  test('metadata identities are not mistaken for playable audio and unknown providers can be added', () => {
    const metadata = playbackTrackFromSong(song({
      sourceIdentities: [{ source: 'musicbrainz', externalId: 'recording' }],
      platformLinks: [{ platform: 'musicbrainz', label: 'View on MusicBrainz', url: 'https://musicbrainz.org/recording/recording' }],
    }));
    const state = apply({ type: 'replaceQueue', tracks: [metadata] }, { type: 'select', index: 0, play: true });
    expect(state.status).toBe('paused');
    expect(playbackAvailability(state)).toEqual({
      canPlayInApp: false, reason: 'No playback source is available for this track.',
      externalUrl: undefined, externalLabel: undefined,
    });

    const custom = playbackTrackFromSong(song({ sourceIdentities: [{ source: 'future', externalId: 'f1' }] }), {
      future: (identity) => ({
        id: `future:${identity.externalId}`, providerId: 'future', label: 'Future',
        capability: { kind: 'embed', mediaId: identity.externalId },
      }),
    });
    expect(custom.sources[0]?.capability).toEqual({ kind: 'embed', mediaId: 'f1' });
  });

  test('multiple identities for one provider keep their own external links', () => {
    const track = playbackTrackFromSong(song({
      sourceIdentities: [
        { source: 'youtube', externalId: 'video-a' },
        { source: 'youtube', externalId: 'video-ab' },
      ],
      platformLinks: [
        { platform: 'youtube', label: 'Watch on YouTube', url: 'https://www.youtube.com/watch?v=video-a' },
        { platform: 'youtube', label: 'Watch on YouTube', url: 'https://www.youtube.com/watch?v=video-ab' },
      ],
    }));
    expect(track.sources.map((source) => source.capability)).toEqual([
      { kind: 'embed', mediaId: 'video-a', externalUrl: 'https://www.youtube.com/watch?v=video-a' },
      { kind: 'embed', mediaId: 'video-ab', externalUrl: 'https://www.youtube.com/watch?v=video-ab' },
    ]);
  });
});
