import { afterEach, beforeEach, describe, expect, mock, test } from 'bun:test';
import type { YouTubeIframeApi, YouTubeIframePlayer } from '../src/music/playback/youtubeIframeApi';
import { mountYouTubePlayback, type YouTubePlaybackSnapshot } from '../src/music/playback/youtubePlayback';

const originalDocument = Object.getOwnPropertyDescriptor(globalThis, 'document');
const originalWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
const mediaId = 'M7lc1UVf-VE';

function setup(autoPlay = true, visible = true) {
  const iframe = {
    remove: mock(() => {}),
    getBoundingClientRect: () => ({ top: 0, left: 0, bottom: 270, right: 480, width: 480, height: 270 }),
    src: '',
  };
  Object.defineProperty(globalThis, 'document', {
    configurable: true, value: { createElement: () => iframe, visibilityState: visible ? 'visible' : 'hidden' },
  });
  const append = mock(() => {});
  const container = { append } as unknown as HTMLElement;
  const onSnapshot = mock<(snapshot: YouTubePlaybackSnapshot) => void>(() => {});
  const onEnded = mock(() => {});
  const callbacks = { autoPlay, onSnapshot, onEnded };
  let events: ConstructorParameters<YouTubeIframeApi['Player']>[1]['events'];
  const rawPlayer = {
    playVideo: mock(() => {}),
    pauseVideo: mock(() => {}),
    seekTo: mock<YouTubeIframePlayer['seekTo']>(() => {}),
    getCurrentTime: mock(() => 0),
    getDuration: mock(() => 210),
    destroy: mock(() => {}),
  } satisfies YouTubeIframePlayer;

  const api = {
    Player: class {
      constructor(_iframe: HTMLIFrameElement, config: ConstructorParameters<YouTubeIframeApi['Player']>[1]) {
        events = config.events;
        return rawPlayer;
      }
    } as unknown as YouTubeIframeApi['Player'],
  };
  return {
    iframe, append, container, callbacks, rawPlayer, api,
    latest: () => onSnapshot.mock.lastCall?.[0],
    ready: () => events.onReady({ target: rawPlayer }),
    state: (data: number) => events.onStateChange({ target: rawPlayer, data }),
    error: (data: number) => events.onError({ target: rawPlayer, data }),
    blocked: () => events.onAutoplayBlocked({ target: rawPlayer }),
  };
}

beforeEach(() => {
  Object.defineProperty(globalThis, 'window', {
    configurable: true, value: { location: { origin: 'http://localhost:5173' }, innerWidth: 800, innerHeight: 600 },
  });
});

afterEach(() => {
  if (originalDocument) Object.defineProperty(globalThis, 'document', originalDocument);
  else Reflect.deleteProperty(globalThis, 'document');
  if (originalWindow) Object.defineProperty(globalThis, 'window', originalWindow);
  else Reflect.deleteProperty(globalThis, 'window');
});

describe('YouTube iframe transport', () => {
  test('waits for ready, reflects actual state and progress, clamps seek, and signals end once', async () => {
    const fixture = setup();
    const player = mountYouTubePlayback(fixture.container, mediaId, fixture.callbacks, async () => fixture.api);
    try {
      await Promise.resolve();
      expect(fixture.iframe.src).toBe('https://www.youtube.com/embed/M7lc1UVf-VE?enablejsapi=1&origin=http%3A%2F%2Flocalhost%3A5173&playsinline=1&rel=0');
      expect(fixture.rawPlayer.playVideo).not.toHaveBeenCalled();
      fixture.ready();
      expect(fixture.rawPlayer.playVideo).toHaveBeenCalledTimes(1);
      expect(fixture.latest()?.phase).toBe('starting');
      fixture.state(3);
      expect(fixture.latest()?.phase).toBe('buffering');
      fixture.state(1);
      expect(fixture.latest()?.phase).toBe('playing');
      player.pause();
      expect(fixture.rawPlayer.pauseVideo).toHaveBeenCalledTimes(1);
      expect(fixture.latest()?.phase).toBe('playing');
      fixture.state(2);
      expect(fixture.latest()?.phase).toBe('paused');

      player.seek(400);
      player.seek(Number.NaN);
      expect(fixture.rawPlayer.seekTo.mock.calls).toEqual([[210, true]]);
      fixture.rawPlayer.getCurrentTime.mockReturnValue(98.9);
      await Bun.sleep(550);
      expect(fixture.latest()).toMatchObject({ phase: 'paused', positionSeconds: 98, durationSeconds: 210 });

      fixture.state(0);
      fixture.state(0);
      expect(fixture.latest()?.phase).toBe('ended');
      expect(fixture.callbacks.onEnded).toHaveBeenCalledTimes(1);
    } finally {
      player.dispose();
    }
    expect(fixture.rawPlayer.destroy).toHaveBeenCalledTimes(1);
    fixture.state(1);
    fixture.error(150);
    expect(fixture.latest()?.phase).toBe('ended');
  });

  test('autoplay blocked permits a later manual play', async () => {
    const fixture = setup();
    const player = mountYouTubePlayback(fixture.container, mediaId, fixture.callbacks, async () => fixture.api);
    try {
      await Promise.resolve();
      fixture.ready();
      fixture.blocked();
      expect(fixture.latest()?.phase).toBe('blocked');
      player.play();
      fixture.state(1);
      expect(fixture.rawPlayer.playVideo).toHaveBeenCalledTimes(2);
      expect(fixture.latest()?.phase).toBe('playing');
    } finally {
      player.dispose();
    }
  });

  test.each([[false, true, 'paused'], [true, false, 'blocked']] as const)(
    'does not autoplay when intent is %s and visibility is %s',
    async (autoPlay, visible, phase) => {
      const fixture = setup(autoPlay, visible);
      const player = mountYouTubePlayback(fixture.container, mediaId, fixture.callbacks, async () => fixture.api);
      try {
        await Promise.resolve();
        fixture.ready();
        expect(fixture.latest()?.phase).toBe(phase);
        expect(fixture.rawPlayer.playVideo).not.toHaveBeenCalled();
      } finally {
        player.dispose();
      }
    },
  );

  test.each([
    [100, 'This video is unavailable on YouTube.'],
    [101, 'YouTube does not allow this video to play here.'],
    [150, 'YouTube does not allow this video to play here.'],
    [153, 'The YouTube player could not load.'],
  ] as const)('reports embed error %d and removes the iframe', async (code, message) => {
    const fixture = setup();
    const player = mountYouTubePlayback(fixture.container, mediaId, fixture.callbacks, async () => fixture.api);
    try {
      await Promise.resolve();
      fixture.error(code);
      expect(fixture.latest()).toMatchObject({ phase: 'error', error: message });
      expect(fixture.rawPlayer.destroy).toHaveBeenCalledTimes(1);
      expect(fixture.iframe.remove).toHaveBeenCalledTimes(1);
      fixture.ready();
      expect(fixture.rawPlayer.playVideo).not.toHaveBeenCalled();
    } finally {
      player.dispose();
    }
    expect(fixture.rawPlayer.destroy).toHaveBeenCalledTimes(1);
  });

  test('reports script load failure and ignores a late script after disposal', async () => {
    const fixture = setup();
    const failed = mountYouTubePlayback(fixture.container, mediaId, fixture.callbacks, async () => {
      throw new Error('network error');
    });
    await Bun.sleep(0);
    expect(fixture.latest()).toMatchObject({ phase: 'error', error: 'The YouTube player could not load.' });
    failed.dispose();

    const late = mountYouTubePlayback(fixture.container, mediaId, fixture.callbacks, async () => fixture.api);
    late.dispose();
    await Promise.resolve();
    expect(fixture.append).not.toHaveBeenCalled();
  });
});
