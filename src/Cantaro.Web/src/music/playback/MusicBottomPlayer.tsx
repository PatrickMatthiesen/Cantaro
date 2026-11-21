import { Pause, Play, SkipBack, SkipForward, Square } from 'lucide-react';
import { useEffectEvent, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { formatDuration } from '../musicPresentation';
import { MusicArtwork } from '../MusicArtwork';
import { useMusicPlayback } from './musicPlaybackContextValue';
import {
  currentPlaybackTrack, isPlayerVisible, playbackAvailability, selectedPlaybackSource,
  type PlaybackAvailability, type PlaybackSource, type PlaybackTrack,
} from './playbackState';
import { loadingYouTubePlayback, mountYouTubePlayback, type YouTubePlaybackSnapshot } from './youtubePlayback';

function PlayerButton({ label, disabled, onClick, children }: {
  label: string;
  disabled?: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      aria-disabled={disabled || undefined}
      onClick={disabled ? undefined : onClick}
      className="inline-flex size-11 shrink-0 items-center justify-center text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus aria-disabled:opacity-40"
    >
      {children}
    </button>
  );
}

type PlaybackPhase = YouTubePlaybackSnapshot['phase'];

const phaseMessages: Partial<Record<PlaybackPhase, string>> = {
  loading: 'Loading YouTube player…',
  starting: 'Starting playback…',
  buffering: 'Buffering…',
  blocked: 'Press Play to start playback.',
  ended: 'Playback ended.',
};

const phaseIntents: Partial<Record<PlaybackPhase, 'play' | 'pause'>> = {
  starting: 'play',
  playing: 'play',
  paused: 'pause',
  blocked: 'pause',
  error: 'pause',
};

const pausablePhases = new Set<PlaybackPhase>(['playing', 'buffering', 'starting']);

function useYouTubeTransport(source: PlaybackSource | null) {
  const { state, dispatch } = useMusicPlayback();
  const { selectionVersion } = state;
  const mediaId = source?.providerId === 'youtube' && source.capability.kind === 'embed' ? source.capability.mediaId : undefined;
  const [observed, setObserved] = useState<{ selectionVersion: number; snapshot: YouTubePlaybackSnapshot }>();
  const mount = useRef<HTMLDivElement>(null);
  const transport = useRef<ReturnType<typeof mountYouTubePlayback> | null>(null);

  const connect = useEffectEvent((container: HTMLDivElement, videoId: string) => {
    let phase = loadingYouTubePlayback.phase;
    return mountYouTubePlayback(container, videoId, {
      autoPlay: state.status === 'playing',
      onSnapshot: (update) => {
        setObserved({ selectionVersion, snapshot: update });
        if (phase === update.phase) return;
        phase = update.phase;
        const intent = phaseIntents[phase];
        if (intent) dispatch({ type: intent, selectionVersion });
      },
      onEnded: () => dispatch({ type: 'ended', selectionVersion }),
    });
  });

  useLayoutEffect(() => {
    if (!mediaId || !mount.current) return;
    const player = connect(mount.current, mediaId);
    transport.current = player;
    return () => {
      player.dispose();
      transport.current = null;
    };
  }, [mediaId, selectionVersion]);

  const snapshot = observed?.selectionVersion === selectionVersion ? observed.snapshot : loadingYouTubePlayback;
  return { mediaId, snapshot, mount, transport };
}

function PlaybackProgress({ positionSeconds, durationSeconds, disabled, onSeek }: {
  positionSeconds: number;
  durationSeconds: number;
  disabled: boolean;
  onSeek: (seconds: number) => void;
}) {
  const position = formatDuration(positionSeconds);
  const duration = durationSeconds ? formatDuration(durationSeconds) : '—';
  return (
    <div className="flex min-w-0 grow basis-48 items-center gap-2 font-mono text-xs text-content-muted">
      <span aria-hidden>{position}</span>
      <input
        type="range"
        aria-label="Playback position"
        aria-valuetext={durationSeconds ? `${position} of ${duration}` : position}
        min={0}
        max={durationSeconds || 1}
        value={positionSeconds}
        disabled={disabled || durationSeconds === 0}
        onChange={(event) => onSeek(event.currentTarget.valueAsNumber)}
        className="h-8 min-w-0 grow accent-personal-accent disabled:opacity-40"
      />
      <span aria-hidden>{duration}</span>
    </div>
  );
}

function playbackNotice({ availability, mediaId, snapshot, showEmbed }: {
  availability: PlaybackAvailability;
  mediaId?: string;
  snapshot: YouTubePlaybackSnapshot;
  showEmbed: boolean;
}) {
  const reason = mediaId
    ? snapshot.error ?? phaseMessages[snapshot.phase]
    : availability.reason ?? 'In-app playback is unavailable.';
  return { reason, externalUrl: showEmbed ? undefined : availability.externalUrl, externalLabel: availability.externalLabel };
}

function PlaybackNotice({ reason, externalUrl, externalLabel }: ReturnType<typeof playbackNotice>) {
  if (!reason) return null;
  return (
    <div role="status" className="flex w-full flex-wrap items-center gap-x-3 text-xs text-content-muted">
      <span>{reason}</span>
      {externalUrl && externalLabel ? (
        <a
          href={externalUrl}
          target="_blank"
          rel="noreferrer"
          className="py-1 font-bold text-personal-accent-strong underline focus-visible:outline-2 focus-visible:outline-focus"
        >
          Open on {externalLabel}
        </a>
      ) : null}
    </div>
  );
}

function PlayerControls({ index, phase, canControl, transport }: {
  index: number;
  phase: PlaybackPhase;
  canControl: boolean;
  transport: ReturnType<typeof useYouTubeTransport>['transport'];
}) {
  const { state, dispatch } = useMusicPlayback();
  const showPause = pausablePhases.has(phase);
  return (
    <div className="flex items-center">
      <PlayerButton label="Previous track" disabled={index === 0} onClick={() => dispatch({ type: 'previous' })}>
        <SkipBack size={18} aria-hidden />
      </PlayerButton>
      <PlayerButton
        label={showPause ? 'Pause' : 'Play'}
        disabled={!canControl}
        onClick={() => showPause ? transport.current?.pause() : transport.current?.play()}
      >
        {showPause ? <Pause size={20} aria-hidden /> : <Play size={20} aria-hidden />}
      </PlayerButton>
      <PlayerButton label="Next track" disabled={index + 1 >= state.queue.length} onClick={() => dispatch({ type: 'next' })}>
        <SkipForward size={18} aria-hidden />
      </PlayerButton>
      <PlayerButton label="Stop playback and close player" onClick={() => dispatch({ type: 'stop' })}>
        <Square size={17} aria-hidden />
      </PlayerButton>
    </div>
  );
}

function ActiveMusicPlayer({ track, source, index }: {
  track: PlaybackTrack;
  source: PlaybackSource | null;
  index: number;
}) {
  const { state } = useMusicPlayback();
  const { mediaId, snapshot, mount, transport } = useYouTubeTransport(source);
  const showEmbed = Boolean(mediaId && snapshot.phase !== 'error');
  const canControl = showEmbed && snapshot.phase !== 'loading';
  const notice = playbackNotice({ availability: playbackAvailability(state), mediaId, snapshot, showEmbed });

  return (
    <section
      aria-label="Music player"
      className="sticky bottom-0 z-25 border-t border-border-subtle bg-surface text-content shadow-xl sm:ml-20 lg:ml-40"
    >
      {showEmbed ? (
        <div className="flex justify-center bg-canvas">
          <div ref={mount} className="h-[min(32dvh,270px)] min-h-50 w-full max-w-120" />
        </div>
      ) : null}
      <div className="mx-auto flex max-w-360 flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2 sm:px-4">
        <div className="flex min-w-0 grow basis-36 items-center gap-3">
          <MusicArtwork src={track.artworkUrl} fallbacks={track.artworkFallbacks} className="size-10" />
          <div className="min-w-0">
            <p className="truncate text-sm font-bold">{track.title}</p>
            <p className="truncate text-xs text-content-muted">
              {[track.artist, source?.label].filter(Boolean).join(' · ')}
            </p>
          </div>
        </div>

        <PlayerControls index={index} phase={snapshot.phase} canControl={canControl} transport={transport} />

        {showEmbed ? (
          <PlaybackProgress
            positionSeconds={snapshot.positionSeconds}
            durationSeconds={snapshot.durationSeconds}
            disabled={!canControl}
            onSeek={(seconds) => transport.current?.seek(seconds)}
          />
        ) : null}

        <PlaybackNotice {...notice} />
      </div>
    </section>
  );
}

export function MusicBottomPlayer() {
  const { state } = useMusicPlayback();
  const track = currentPlaybackTrack(state);
  const source = selectedPlaybackSource(state);
  if (!isPlayerVisible(state) || !track || state.currentIndex === null) return null;

  return (
    <ActiveMusicPlayer
      track={track}
      source={source}
      index={state.currentIndex}
    />
  );
}
