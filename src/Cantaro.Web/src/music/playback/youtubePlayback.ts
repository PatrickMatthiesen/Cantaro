import { loadYouTubeIframeApi, type YouTubeIframePlayer } from './youtubeIframeApi';

type PlaybackPhase = 'loading' | 'paused' | 'starting' | 'playing' | 'buffering' | 'ended' | 'blocked' | 'error';

export interface YouTubePlaybackSnapshot {
  phase: PlaybackPhase;
  positionSeconds: number;
  durationSeconds: number;
  error?: string;
}

export const loadingYouTubePlayback: YouTubePlaybackSnapshot = {
  phase: 'loading',
  positionSeconds: 0,
  durationSeconds: 0,
};

const unavailable = 'This video is unavailable on YouTube.';
const restricted = 'YouTube does not allow this video to play here.';
const failed = 'The YouTube player could not load.';

function errorMessage(code: number): string {
  if (code === 100) return unavailable;
  if (code === 101 || code === 150) return restricted;
  return failed;
}

function playbackPhase(code: number): PlaybackPhase | undefined {
  switch (code) {
    case 0: return 'ended';
    case 1: return 'playing';
    case 2: return 'paused';
    case 3: return 'buffering';
    case 5: return 'paused';
    default: return undefined;
  }
}

function seconds(value: number): number {
  return Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;
}

function isVisible(iframe: HTMLIFrameElement): boolean {
  const { top, left, bottom, right, width, height } = iframe.getBoundingClientRect();
  const visibleWidth = Math.min(right, window.innerWidth) - Math.max(left, 0);
  const visibleHeight = Math.min(bottom, window.innerHeight) - Math.max(top, 0);
  return document.visibilityState === 'visible'
    && visibleWidth > 0 && visibleHeight > 0
    && visibleWidth * visibleHeight > width * height / 2;
}

export function mountYouTubePlayback(
  container: HTMLElement,
  mediaId: string,
  { autoPlay, onSnapshot, onEnded }: {
    autoPlay: boolean;
    onSnapshot: (snapshot: YouTubePlaybackSnapshot) => void;
    onEnded: () => void;
  },
  loadApi = loadYouTubeIframeApi,
) {
  let player: YouTubeIframePlayer | undefined;
  let ready = false;
  let disposed = false;
  let snapshot = loadingYouTubePlayback;
  let timeout: ReturnType<typeof setTimeout> | undefined;
  let progressTimer: ReturnType<typeof setInterval> | undefined;

  const iframe = document.createElement('iframe');
  iframe.title = 'YouTube video player';
  iframe.allow = 'autoplay; encrypted-media; picture-in-picture; fullscreen';
  iframe.allowFullscreen = true;
  iframe.referrerPolicy = 'strict-origin-when-cross-origin';
  iframe.className = 'block h-full w-full border-0';

  function publish(phase: PlaybackPhase = snapshot.phase, error?: string) {
    const durationSeconds = seconds(ready ? player?.getDuration() ?? 0 : 0);
    const position = seconds(ready ? player?.getCurrentTime() ?? 0 : 0);
    const positionSeconds = durationSeconds > 0 ? Math.min(position, durationSeconds) : position;
    if (phase === snapshot.phase && positionSeconds === snapshot.positionSeconds
      && durationSeconds === snapshot.durationSeconds && error === snapshot.error) return;
    snapshot = { phase, positionSeconds, durationSeconds, error };
    onSnapshot(snapshot);
  }

  function destroy() {
    disposed = true;
    clearTimeout(timeout);
    clearInterval(progressTimer);
    player?.destroy();
    iframe.remove();
  }

  function fail(message: string) {
    if (disposed) return;
    ready = false;
    destroy();
    publish('error', message);
  }

  function play() {
    if (!ready || disposed) return;
    publish('starting');
    clearTimeout(timeout);
    timeout = setTimeout(() => fail('YouTube playback did not start.'), 15_000);
    player?.playVideo();
  }

  void loadApi().then((api) => {
    if (disposed) return;
    const params = new URLSearchParams({ enablejsapi: '1', origin: window.location.origin, playsinline: '1', rel: '0' });
    iframe.src = `https://www.youtube.com/embed/${encodeURIComponent(mediaId)}?${params}`;
    container.append(iframe);
    timeout = setTimeout(() => fail(failed), 12_000);
    player = new api.Player(iframe, {
      events: {
        onReady: ({ target }) => {
          if (disposed) return;
          player = target;
          ready = true;
          clearTimeout(timeout);
          publish('paused');
          progressTimer = setInterval(() => publish(), 500);
          if (!autoPlay) return;
          if (isVisible(iframe)) play();
          else publish('blocked');
        },
        onStateChange: ({ data }) => {
          if (!ready || disposed) return;
          const phase = playbackPhase(data);
          if (!phase) return;
          // The embed can become cued while a play request is still pending.
          if (data === 5 && snapshot.phase === 'starting') return;
          if (phase !== 'buffering') clearTimeout(timeout);
          const didEnd = phase === 'ended' && snapshot.phase !== 'ended';
          publish(phase);
          if (didEnd) onEnded();
        },
        onAutoplayBlocked: () => {
          if (!ready || disposed) return;
          clearTimeout(timeout);
          publish('blocked');
        },
        onError: ({ data }) => fail(errorMessage(data)),
      },
    });
  }).catch(() => fail(failed));

  return {
    play,
    pause() {
      if (!ready || disposed) return;
      clearTimeout(timeout);
      player?.pauseVideo();
    },
    seek(positionSeconds: number) {
      if (!ready || disposed || !snapshot.durationSeconds || !Number.isFinite(positionSeconds)) return;
      player?.seekTo(Math.max(0, Math.min(positionSeconds, snapshot.durationSeconds)), true);
    },
    dispose() {
      if (!disposed) destroy();
    },
  };
}
