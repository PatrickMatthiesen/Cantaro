import { LyricsResultContent } from './LyricsResultContent';
import { useYouTubeLyrics, type CurrentYouTubeState } from './useYouTubeLyrics';

export function YouTubeLyricsCard({ enabled }: { enabled: boolean }) {
  const current = useYouTubeLyrics(enabled);
  if (current.kind === 'disabled' || current.kind === 'checking' || current.kind === 'unsupported') return null;
  return <section className="border border-border-subtle bg-surface px-3 py-3" aria-label="Current YouTube song">
    <CurrentLyricsContent current={current} />
  </section>;
}

function CurrentLyricsContent({ current }: { current: CurrentYouTubeState }) {
  switch (current.kind) {
    case 'loading': return <p className="text-sm text-content-muted">Finding the current song…</p>;
    case 'unmatched': return <p className="text-sm text-content-muted">This video is not linked to a song in your Cantaro library.</p>;
    case 'error': return <p className="text-sm text-content-muted" role="status">{current.message}</p>;
    case 'song': return <CurrentSongLyrics current={current} />;
    default: return null;
  }
}

function CurrentSongLyrics({ current }: { current: Extract<CurrentYouTubeState, { kind: 'song' }> }) {
  return <>
    <p className="truncate text-sm font-bold text-content">{current.song.title}</p>
    {current.song.artist ? <p className="mt-0.5 truncate text-xs text-content-muted">{current.song.artist}</p> : null}
    <details key={current.videoId} className="mt-2 border-t border-border-subtle pt-2">
      <summary className="cursor-pointer text-xs font-semibold text-content hover:text-personal-accent focus-visible:outline-2 focus-visible:outline-focus">
        Show lyrics
      </summary>
      <div className="mt-2">{current.lyrics ? <LyricsResultContent result={current.lyrics} /> : <p>Lyrics are unavailable for this song.</p>}</div>
    </details>
  </>;
}
