import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import type { MusicTabContext } from '../../features/music/contracts/musicTabContext';
import type { MusicContextResolution } from './musicMatching';
import {
  openCantaroPage,
  type MusicRecognitionResult,
} from './musicService';

interface CurrentMusicTabProps {
  context: MusicTabContext;
  resolution: MusicContextResolution | null;
  recognition: MusicRecognitionResult | null;
  loading: boolean;
  error: string | null;
  onSelectSong: (song: MusicLibrarySong) => void;
  onRetry: () => void;
}

export function CurrentMusicTab(props: CurrentMusicTabProps) {
  return (
    <section className="rounded-xl bg-slate-950 p-3 text-white" aria-labelledby="current-music-tab-title">
      <p id="current-music-tab-title" className="text-xs font-semibold text-violet-300">Current tab</p>
      <CurrentMusicTabContent {...props} />
    </section>
  );
}

function CurrentMusicTabContent(props: CurrentMusicTabProps) {
  const match = resolvedSong(props);
  if (match) return <MatchedSong song={match.song} onSelect={props.onSelectSong} recognized={match.recognized} />;
  if (props.context.pageKind === 'placeholder') {
    return <p className="mt-1 text-xs text-slate-300">{props.context.message || 'Cantaro recognizes this provider, but no track controls are available here yet.'}</p>;
  }
  return (
    <RecognitionState
      loading={props.loading}
      error={props.error}
      recognition={props.recognition}
      onRetry={props.onRetry}
    />
  );
}

function resolvedSong(props: CurrentMusicTabProps): { song: MusicLibrarySong; recognized: boolean } | null {
  if (props.resolution?.status === 'matched') return { song: props.resolution.song, recognized: false };
  if (props.recognition?.status === 'matched' && props.recognition.song) {
    return { song: props.recognition.song, recognized: true };
  }
  return null;
}

function MatchedSong({ song, onSelect, recognized = false }: {
  song: MusicLibrarySong;
  onSelect: (song: MusicLibrarySong) => void;
  recognized?: boolean;
}) {
  return (
    <div className="mt-1 flex items-end gap-3">
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-bold">{song.title}</p>
        <p className="mt-0.5 truncate text-xs font-semibold text-violet-200">{song.artist || 'Unknown artist'}</p>
        {recognized ? <p className="mt-0.5 text-xs text-slate-300">Matched by Cantaro</p> : null}
      </div>
      <div className="flex shrink-0 gap-2">
        <button type="button" className="rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => onSelect(song)}>View</button>
        <button type="button" className="rounded-lg border border-white/25 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/10" onClick={() => void openCantaroPage(`/music/songs/${encodeURIComponent(song.id)}`)}>Open in Cantaro</button>
      </div>
    </div>
  );
}

function RecognitionState({ loading, error, recognition, onRetry }: {
  loading: boolean;
  error: string | null;
  recognition: MusicRecognitionResult | null;
  onRetry: () => void;
}) {
  if (loading) return <p className="mt-1 text-xs text-slate-300">Checking whether this page is music…</p>;
  if (recognition?.classification === 'music') return <MusicNeedsReview recognition={recognition} />;
  if (recognition?.classification === 'not_music') {
    return <p className="mt-1 text-xs text-slate-300">This YouTube page is not categorized as music, so Cantaro left it alone.</p>;
  }
  return <RecognitionFailure error={error} onRetry={onRetry} />;
}

function MusicNeedsReview({ recognition }: { recognition: MusicRecognitionResult }) {
  const detail = recognition.status === 'pending'
    ? 'Cantaro is matching this song in the background. You can close the popup.'
    : 'Cantaro recognized a song, but could not match it automatically.';
  return (
    <div className="mt-1 flex items-end gap-3">
      <div className="min-w-0 flex-1">
        <p className="text-xs text-slate-300">{detail}</p>
        {recognition.title ? <p className="mt-1 truncate text-xs font-semibold text-violet-200">{recognition.title}{recognition.artist ? ` · ${recognition.artist}` : ''}</p> : null}
      </div>
      <button type="button" className="shrink-0 rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => void openCantaroPage('/music/matching')}>Review matching</button>
    </div>
  );
}

function RecognitionFailure({ error, onRetry }: { error: string | null; onRetry: () => void }) {
  return (
    <div className="mt-1 flex items-end gap-3">
      <div className="min-w-0 flex-1">
        <p className="text-xs text-slate-300">{error ?? 'Cantaro could not read this page’s music category.'}</p>
        <p className="mt-1 text-[11px] text-slate-400">Reconnect YouTube, then check the page again.</p>
      </div>
      <div className="flex shrink-0 gap-2">
        <button type="button" className="rounded-lg border border-white/25 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/10" onClick={onRetry}>Try again</button>
        <button type="button" className="rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => void openCantaroPage('/music/platforms/youtube')}>Reconnect</button>
      </div>
    </div>
  );
}
