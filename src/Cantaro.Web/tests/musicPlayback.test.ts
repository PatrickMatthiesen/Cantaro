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
  test('playing a song preserves the manually queued tracks and the active track remains in history', () => {
    const queued = apply({ type: 'enqueue', track: spotify }, { type: 'enqueue', track: unavailable });
    const playing = playbackReducer(queued, { type: 'enqueue', play: true, track: youtube });
    expect(currentPlaybackTrack(playing)).toBe(youtube);
    expect(playing.status).toBe('playing');
    expect(playing.queue).toEqual([youtube, spotify, unavailable]);

    const next = playbackReducer(playing, { type: 'enqueue', play: true, track: spotify });
    expect(next.queue).toEqual([youtube, spotify, spotify, unavailable]);
    expect(next.currentIndex).toBe(1);
    expect(next.status).toBe('paused');
  });

  test('playing a collection selects its first track and end advances only the matching selection', () => {
    const first = apply({ type: 'replaceQueue', play: true, tracks: [youtube, youtube] });
    expect(first.currentIndex).toBe(0);
    expect(first.status).toBe('playing');
    const end: PlaybackAction = { type: 'ended', selectionVersion: first.selectionVersion };
    const second = playbackReducer(first, end);
    expect(second.currentIndex).toBe(1);
    expect(second.status).toBe('playing');
    expect(playbackReducer(second, end)).toBe(second);
    expect(playbackReducer(second, { type: 'pause', selectionVersion: first.selectionVersion })).toBe(second);
    const finished = playbackReducer(second, { type: 'ended', selectionVersion: second.selectionVersion });
    expect(finished.currentIndex).toBe(1);
    expect(finished.status).toBe('paused');
    expect(isPlayerVisible(finished)).toBe(true);
  });

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

  test('moving directly between embeddable tracks preserves play intent in both directions', () => {
    const nextTrack: PlaybackTrack = {
      id: 'another-embed', title: 'Another embed', sources: [{
        id: 'youtube:another', providerId: 'youtube', label: 'YouTube',
        capability: { kind: 'embed', mediaId: 'another' },
      }],
    };
    const started = apply(
      { type: 'replaceQueue', tracks: [youtube, nextTrack] },
      { type: 'select', index: 0, play: true },
    );

    const next = playbackReducer(started, { type: 'next' });
    expect(currentPlaybackTrack(next)?.id).toBe('another-embed');
    expect(selectedPlaybackSource(next)?.id).toBe('youtube:another');
    expect(next.status).toBe('playing');

    const previous = playbackReducer(next, { type: 'previous' });
    expect(currentPlaybackTrack(previous)?.id).toBe('first');
    expect(selectedPlaybackSource(previous)?.id).toBe('youtube:one');
    expect(previous.status).toBe('playing');
  });

  test('enqueueing retains the current track, source, and status', () => {
    const started = apply(
      { type: 'replaceQueue', tracks: [spotify, youtube] },
      { type: 'select', index: 1, play: true },
    );

    const enqueued = playbackReducer(started, { type: 'enqueue', track: unavailable });
    expect(enqueued.queue.map((track) => track.id)).toEqual(['second', 'first', 'third']);
    expect(enqueued.currentIndex).toBe(started.currentIndex);
    expect(currentPlaybackTrack(enqueued)).toBe(youtube);
    expect(enqueued.selectedSourceId).toBe(started.selectedSourceId);
    expect(selectedPlaybackSource(enqueued)).toBe(youtube.sources[0]);
    expect(enqueued.status).toBe('playing');
  });

  test('clearing up next keeps the active track, selected source, and session intent', () => {
    const active: PlaybackTrack = {
      ...youtube,
      sources: [
        ...youtube.sources,
        {
          id: 'youtube:alternate', providerId: 'youtube', label: 'Alternate video',
          capability: { kind: 'embed', mediaId: 'alternate' },
        },
      ],
    };
    const selected = apply(
      { type: 'replaceQueue', tracks: [spotify, active, unavailable, youtube] },
      { type: 'select', index: 1 },
      { type: 'selectSource', sourceId: 'youtube:alternate' },
      { type: 'play' },
    );

    for (const session of [
      selected,
      playbackReducer(selected, { type: 'pause' }),
    ]) {
      const cleared = playbackReducer(session, { type: 'clearQueue' });
      expect(cleared).toEqual({ ...session, queue: [spotify, active] });
      expect(currentPlaybackTrack(cleared)).toBe(active);
      expect(selectedPlaybackSource(cleared)?.id).toBe('youtube:alternate');
      expect(isPlayerVisible(cleared)).toBe(true);
      expect(playbackReducer(cleared, { type: 'next' })).toEqual(cleared);
      expect(currentPlaybackTrack(playbackReducer(cleared, { type: 'previous' }))).toBe(spotify);
    }
  });

  test('clearing an unselected queue empties it; replacing with an empty queue resets the session', () => {
    const queued = apply({ type: 'replaceQueue', tracks: [youtube, spotify] });
    expect(playbackReducer(queued, { type: 'clearQueue' })).toEqual(initialPlaybackState);

    const playing = playbackReducer(queued, { type: 'select', index: 0, play: true });
    const reset = playbackReducer(playing, { type: 'replaceQueue', tracks: [] });
    expect(reset).toEqual(initialPlaybackState);
    expect(isPlayerVisible(reset)).toBe(false);
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
    expect(playbackReducer(paused, { type: 'stop' })).toEqual(initialPlaybackState);
    expect(isPlayerVisible(paused)).toBe(true);
    expect(playbackReducer(paused, { type: 'replaceQueue', tracks: [spotify] }).currentIndex).toBeNull();
    expect(playbackReducer(paused, { type: 'replaceQueue', tracks: [] })).toEqual(initialPlaybackState);
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
  test('a YouTube Music link without a video identity stays an attributed external fallback', () => {
    const url = 'https://music.youtube.com/watch?v=M7lc1UVf-VE';
    const track = playbackTrackFromSong(song({
      platformLinks: [{ platform: 'youtube-music', source: 'youtube', externalId: 'M7lc1UVf-VE', label: 'Open in YouTube Music', url }],
    }));
    expect(playbackAvailability(apply({ type: 'enqueue', play: true, track }))).toEqual({
      canPlayInApp: false, reason: 'YouTube Music opens outside Cantaro.', externalUrl: url, externalLabel: 'YouTube Music',
    });
  });

  test('an invalid YouTube identity uses the attributed external mapping when available', () => {
    const invalid = song({ sourceIdentities: [{ source: 'youtube', externalId: 'broken' }] });
    const withoutLink = apply({ type: 'enqueue', play: true, track: playbackTrackFromSong(invalid) });
    expect(playbackAvailability(withoutLink)).toMatchObject({
      canPlayInApp: false, reason: 'This track has no valid YouTube video ID.', externalUrl: undefined,
    });

    const url = 'https://www.youtube.com/watch?v=broken';
    const track = playbackTrackFromSong({
      ...invalid,
      platformLinks: [{ platform: 'youtube', source: 'youtube', externalId: 'broken', label: 'Watch on YouTube', url }],
    });
    expect(playbackAvailability(apply({ type: 'enqueue', play: true, track }))).toMatchObject({
      canPlayInApp: false, externalUrl: url, externalLabel: 'YouTube',
    });
  });

  test('a known YouTube Music video identity embeds with a YouTube fallback', () => {
    const track = playbackTrackFromSong(song({ sourceIdentities: [{ source: 'youtube-music', externalId: 'M7lc1UVf-VE' }] }));
    expect(selectedPlaybackSource(apply({ type: 'enqueue', play: true, track }))?.capability).toEqual({
      kind: 'embed', mediaId: 'M7lc1UVf-VE', externalUrl: 'https://www.youtube.com/watch?v=M7lc1UVf-VE',
    });
  });

  test('YouTube embeds while Spotify remains an external fallback', () => {
    const track = playbackTrackFromSong(song({
      sourceIdentities: [
        { source: 'spotify', externalId: 'sp1' },
        { source: 'youtube', externalId: 'M7lc1UVf-VE' },
      ],
      platformLinks: [
        { platform: 'spotify', label: 'Listen on Spotify', url: 'https://open.spotify.com/track/sp1' },
        { platform: 'youtube', label: 'Watch on YouTube', url: 'https://www.youtube.com/watch?v=M7lc1UVf-VE' },
      ],
    }));
    const selected = apply({ type: 'replaceQueue', tracks: [track] }, { type: 'select', index: 0, play: true });
    expect(selectedPlaybackSource(selected)?.capability).toEqual({
      kind: 'embed', mediaId: 'M7lc1UVf-VE', externalUrl: 'https://www.youtube.com/watch?v=M7lc1UVf-VE',
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
        { source: 'youtube', externalId: 'M7lc1UVf-VE' },
        { source: 'youtube', externalId: 'aqz-KE-bpKQ' },
      ],
      platformLinks: [
        { platform: 'youtube', label: 'Watch on YouTube', url: 'https://www.youtube.com/watch?v=M7lc1UVf-VE' },
        { platform: 'youtube', label: 'Watch on YouTube', url: 'https://www.youtube.com/watch?v=aqz-KE-bpKQ' },
      ],
    }));
    expect(track.sources.map((source) => source.capability)).toEqual([
      { kind: 'embed', mediaId: 'M7lc1UVf-VE', externalUrl: 'https://www.youtube.com/watch?v=M7lc1UVf-VE' },
      { kind: 'embed', mediaId: 'aqz-KE-bpKQ', externalUrl: 'https://www.youtube.com/watch?v=aqz-KE-bpKQ' },
    ]);
  });
});
