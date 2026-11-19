export interface YouTubeIframePlayer {
  playVideo(): void;
  pauseVideo(): void;
  seekTo(seconds: number, allowSeekAhead: boolean): void;
  getCurrentTime(): number;
  getDuration(): number;
  destroy(): void;
}

type PlayerEvent<T = undefined> = { target: YouTubeIframePlayer } & (T extends undefined ? object : { data: T });

export interface YouTubeIframeApi {
  Player: new (iframe: HTMLIFrameElement, config: {
    events: {
      onReady(event: PlayerEvent): void;
      onStateChange(event: PlayerEvent<number>): void;
      onError(event: PlayerEvent<number>): void;
      onAutoplayBlocked(event: PlayerEvent): void;
    };
  }) => YouTubeIframePlayer;
}

type YouTubeWindow = Window & {
  YT?: YouTubeIframeApi;
  onYouTubeIframeAPIReady?: () => void;
};

let pendingApi: Promise<YouTubeIframeApi> | undefined;

export function loadYouTubeIframeApi(): Promise<YouTubeIframeApi> {
  const host = window as YouTubeWindow;
  if (host.YT?.Player) return Promise.resolve(host.YT);
  if (pendingApi) return pendingApi;

  pendingApi = new Promise<YouTubeIframeApi>((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'https://www.youtube.com/iframe_api';
    script.async = true;

    const previousCallback = host.onYouTubeIframeAPIReady;
    const cleanup = () => {
      window.clearTimeout(timeout);
      if (host.onYouTubeIframeAPIReady === onReady) host.onYouTubeIframeAPIReady = previousCallback;
      script.removeEventListener('error', onError);
      script.remove();
    };
    const onError = () => {
      cleanup();
      reject(new Error('YouTube player API failed to load.'));
    };
    const onReady = () => {
      if (!host.YT?.Player) {
        onError();
        return;
      }
      cleanup();
      resolve(host.YT);
      previousCallback?.();
    };

    const timeout = window.setTimeout(onError, 12_000);
    host.onYouTubeIframeAPIReady = onReady;
    script.addEventListener('error', onError);
    document.head.append(script);
  }).finally(() => {
    pendingApi = undefined;
  });

  return pendingApi;
}
