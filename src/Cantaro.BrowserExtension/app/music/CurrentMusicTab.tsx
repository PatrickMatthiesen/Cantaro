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
    <section className="border-y border-border-subtle bg-surface-subtle px-3 py-3" aria-labelledby="current-music-tab-title">
      <p id="current-music-tab-title" className="text-xs font-semibold text-personal-accent-strong">Current tab</p>
      <CurrentMusicTabContent {...props} />
    </section>
  );
}

function CurrentMusicTabContent(props: CurrentMusicTabProps) {
  const match = resolvedSong(props);
  if (match) return <MatchedSong song={match.song} onSelect={props.onSelectSong} recognized={match.recognized} />;
  if (props.context.pageKind === 'placeholder') {
    return <p className="mt-1 text-xs text-content-muted">{props.context.message || 'Cantaro recognizes this provider, but no track controls are available here yet.'}</p>;
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
        <p className="truncate text-sm font-bold text-content">{song.title}</p>
        <p className="mt-0.5 truncate text-xs font-semibold text-content-muted">{song.artist || 'Unknown artist'}</p>
        {recognized ? <p className="mt-0.5 text-xs text-success-content">Matched by Cantaro</p> : null}
      </div>
      <div className="flex shrink-0 gap-2">
        <button type="button" className="min-h-9 bg-personal-accent px-3 text-xs font-bold text-personal-accent-contrast hover:bg-personal-accent-hover" onClick={() => onSelect(song)}>View</button>
        <button type="button" className="min-h-9 border border-border-strong bg-surface px-3 text-xs font-bold text-content hover:bg-surface-hover" onClick={() => void openCantaroPage(`/music/songs/${encodeURIComponent(song.id)}`)}>Open in Cantaro</button>
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
  if (loading) return <p className="mt-1 text-xs text-content-muted">Checking whether this page is music…</p>;
  if (recognition?.classification === 'music') return <MusicNeedsReview recognition={recognition} />;
  if (recognition?.classification === 'not_music') {
    return <p className="mt-1 text-xs text-content-muted">This YouTube page is not categorized as music, so Cantaro left it alone.</p>;
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
        <p className="text-xs text-content-muted">{detail}</p>
        {recognition.title ? <p className="mt-1 truncate text-xs font-semibold text-content">{recognition.title}{recognition.artist ? ` · ${recognition.artist}` : ''}</p> : null}
      </div>
      <button type="button" className="min-h-9 shrink-0 bg-personal-accent px-3 text-xs font-bold text-personal-accent-contrast hover:bg-personal-accent-hover" onClick={() => void openCantaroPage('/music/matching')}>Review matching</button>
    </div>
  );
}

function RecognitionFailure({ error, onRetry }: { error: string | null; onRetry: () => void }) {
  return (
    <div className="mt-1 flex items-end gap-3">
      <div className="min-w-0 flex-1">
        <p className="text-xs text-content-muted">{error ?? 'Cantaro could not read this page’s music category.'}</p>
        <p className="mt-1 text-[11px] text-content-subtle">Reconnect YouTube, then check the page again.</p>
      </div>
      <div className="flex shrink-0 gap-2">
        <button type="button" className="min-h-9 border border-border-strong bg-surface px-3 text-xs font-bold text-content hover:bg-surface-hover" onClick={onRetry}>Try again</button>
        <button type="button" className="min-h-9 bg-personal-accent px-3 text-xs font-bold text-personal-accent-contrast hover:bg-personal-accent-hover" onClick={() => void openCantaroPage('/music/platforms/youtube')}>Reconnect</button>
      </div>
    </div>
  );
}
