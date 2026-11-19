import type { PlaybackTrack } from './playback/playbackState';
import { formatDuration } from './musicPresentation';
import { MusicArtwork } from './MusicArtwork';

type MusicQueueTrack = Pick<PlaybackTrack, 'id' | 'title' | 'artist' | 'artworkUrl' | 'durationSeconds'>;

function QueueRow({ track, label, active }: { track: MusicQueueTrack; label: string; active?: boolean }) {
  return (
    <div className="grid grid-cols-[44px_minmax(0,1fr)_auto] items-center gap-3">
      <MusicArtwork src={track.artworkUrl} className="size-11" />
      <div className="min-w-0">
        <p className={`text-[0.66rem] font-black tracking-[0.14em] uppercase ${active ? 'text-personal-accent-strong' : 'text-content-muted'}`}>
          {label}
        </p>
        <p className="mt-0.5 truncate text-sm font-black text-content">{track.title}</p>
        <p className="truncate text-xs font-semibold text-content-muted">{track.artist ?? 'Unknown artist'}</p>
      </div>
      <span className="font-mono text-xs text-content-muted">{formatDuration(track.durationSeconds)}</span>
    </div>
  );
}

export function MusicUpNextPanel({
  activeTrack,
  queuedTracks,
  title = 'Up Next',
  onClearQueue,
  onPlayQueuedTrack,
}: {
  activeTrack?: MusicQueueTrack;
  queuedTracks: MusicQueueTrack[];
  title?: string;
  onClearQueue?: () => void;
  onPlayQueuedTrack?: (index: number) => void;
}) {
  const hasContent = Boolean(activeTrack || queuedTracks.length > 0);

  return (
    <section className="border-y border-border-subtle py-4">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="font-black text-content">{title}</h2>
        {onClearQueue ? (
          <button
            type="button"
            className="text-xs font-black text-content-muted transition hover:text-personal-accent-strong disabled:cursor-not-allowed disabled:opacity-45"
            disabled={queuedTracks.length === 0}
            onClick={onClearQueue}
          >
            Clear
          </button>
        ) : null}
      </div>
      <div className="max-h-[420px] space-y-3 overflow-y-auto">
        {activeTrack ? <QueueRow track={activeTrack} label="Now playing" active /> : null}
        {queuedTracks.map((track, index) => {
          const key = `${track.id}:${index}`;
          const label = index === 0 ? 'Up next' : `Later ${index + 1}`;
          return onPlayQueuedTrack ? (
            <button
              key={key}
              type="button"
              aria-label={`Play queued track ${track.title}`}
              className="block w-full text-left hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus"
              onClick={() => onPlayQueuedTrack(index)}
            >
              <QueueRow track={track} label={label} />
            </button>
          ) : <QueueRow key={key} track={track} label={label} />;
        })}
        {!hasContent ? <p className="text-sm font-medium text-content-muted">Play a track or add one to the queue.</p> : null}
      </div>
    </section>
  );
}
