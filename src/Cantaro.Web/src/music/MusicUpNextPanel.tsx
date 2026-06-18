import type { MusicQueueTrack } from './useMusicQueue';
import { formatDuration } from './musicPresentation';

function QueueArtwork({ track }: { track: MusicQueueTrack }) {
  if (track.artworkUrl) {
    return <img src={track.artworkUrl} alt="" className="h-11 w-11 rounded-xl object-cover" />;
  }

  return <div className="h-11 w-11 rounded-xl bg-[linear-gradient(135deg,#172554,#7c3aed_52%,#fb7185)]" aria-hidden />;
}

function QueueRow({ track, label, active }: { track: MusicQueueTrack; label: string; active?: boolean }) {
  return (
    <div className="grid grid-cols-[44px_minmax(0,1fr)_auto] items-center gap-3">
      <QueueArtwork track={track} />
      <div className="min-w-0">
        <p className={`text-[0.66rem] font-black tracking-[0.14em] uppercase ${active ? 'text-violet-600' : 'text-slate-500'}`}>
          {label}
        </p>
        <p className="mt-0.5 truncate text-sm font-black text-slate-950">{track.title}</p>
        <p className="truncate text-xs font-semibold text-slate-500">{track.artist ?? 'Unknown artist'}</p>
      </div>
      <span className="font-mono text-xs text-slate-500">{formatDuration(track.durationSeconds)}</span>
    </div>
  );
}

export function MusicUpNextPanel({
  activeTrack,
  queuedTracks,
  title = 'Up Next',
  onClearQueue,
}: {
  activeTrack?: MusicQueueTrack;
  queuedTracks: MusicQueueTrack[];
  title?: string;
  onClearQueue?: () => void;
}) {
  const hasContent = Boolean(activeTrack || queuedTracks.length > 0);

  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="font-black text-slate-950">{title}</h2>
        {onClearQueue ? (
          <button
            type="button"
            className="text-xs font-black text-violet-600 transition hover:text-violet-500 disabled:cursor-not-allowed disabled:opacity-45"
            disabled={queuedTracks.length === 0}
            onClick={onClearQueue}
          >
            Clear
          </button>
        ) : null}
      </div>
      <div className="max-h-[420px] space-y-3 overflow-y-auto">
        {activeTrack ? <QueueRow track={activeTrack} label="Now playing" active /> : null}
        {queuedTracks.map((track, index) => (
          <QueueRow key={track.id} track={track} label={index === 0 ? 'Up next' : `Later ${index + 1}`} />
        ))}
        {!hasContent ? <p className="text-sm font-medium text-slate-500">Play a track or add one to the queue.</p> : null}
      </div>
    </section>
  );
}
