import { useEffect, useRef, useState } from 'react';
import { displayLyricsText, type LyricsResult } from './musicLyrics';
import { loadSongLyrics } from './musicService';
import { SongSection } from './SongSection';

type LyricsView =
  | { kind: 'loading' }
  | { kind: 'ready'; result: LyricsResult }
  | { kind: 'error'; message: string }
  | { kind: 'unavailable'; message: string };

export function SongLyrics({ trackId }: { trackId: string | null }) {
  const [view, setView] = useState<LyricsView>(() => trackId
    ? { kind: 'loading' }
    : { kind: 'unavailable', message: 'Lyrics are not available for this song.' });
  const requestId = useRef(0);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    const controller = new AbortController();
    if (!trackId) {
      setView({ kind: 'unavailable', message: 'Lyrics are not available for this song.' });
      return () => controller.abort();
    }

    setView({ kind: 'loading' });
    void loadSongLyrics(trackId, controller.signal)
      .then((result) => {
        if (requestId.current === currentRequest) setView({ kind: 'ready', result });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || requestId.current !== currentRequest) return;
        setView({ kind: 'error', message: error instanceof Error ? error.message : 'The lyrics provider could not be reached.' });
      });
    return () => controller.abort();
  }, [trackId]);

  return <SongSection title="Lyrics"><LyricsContent view={view} /></SongSection>;
}

function LyricsContent({ view }: { view: LyricsView }) {
  if (view.kind === 'loading') return <LyricsLoading />;
  if (view.kind === 'ready') return <LyricsResultContent result={view.result} />;
  return <LyricsNotice title={view.kind === 'error' ? 'Lyrics provider unavailable' : 'Lyrics unavailable'} detail={view.message} />;
}

function LyricsLoading() {
  return (
    <div className="space-y-2" aria-label="Loading lyrics">
      <div className="cantaro-lyrics-loading-line h-3 w-11/12 rounded" />
      <div className="cantaro-lyrics-loading-line h-3 w-4/5 rounded" />
      <div className="cantaro-lyrics-loading-line h-3 w-9/12 rounded" />
    </div>
  );
}

const lyricsNotices = {
  available: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
  instrumental: { title: 'Instrumental track', fallback: 'This track has no lyrics.' },
  ambiguous: { title: 'Lyrics need review', fallback: 'Cantaro found more than one possible lyrics match.' },
  provider_error: { title: 'Lyrics provider unavailable', fallback: 'Try again in a moment.' },
  disabled: { title: 'Lyrics are unavailable', fallback: 'Lyrics lookup is disabled for this Cantaro server.' },
  unavailable: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
} satisfies Record<LyricsResult['state'], { title: string; fallback: string }>;

function LyricsResultContent({ result }: { result: LyricsResult }) {
  const selected = result.state === 'available' ? displayLyricsText(result) : null;
  if (!selected) {
    const notice = lyricsNotices[result.state];
    return <LyricsNotice title={notice.title} detail={result.explanation || notice.fallback} attribution={result.attribution} />;
  }

  return (
    <>
      <div className="flex items-baseline justify-between gap-3">
        <p className="text-xs font-semibold text-content">{selected.synchronized ? 'Timed lyrics' : 'Lyrics'}</p>
        <span className="text-[11px] text-content-muted">{result.matchStatus === 'exact' ? 'Matched' : 'Best match'}</span>
      </div>
      <pre className="mt-2 whitespace-pre-wrap break-words font-sans text-sm leading-6 text-content">{selected.text}</pre>
      <p className="mt-3 text-[11px] text-content-muted">{result.attribution}</p>
    </>
  );
}

function LyricsNotice({ title, detail, attribution }: { title: string; detail: string; attribution?: string }) {
  return (
    <>
      <p className="text-sm font-bold text-content">{title}</p>
      <p className="mt-1 text-xs leading-5 text-content-muted">{detail}</p>
      {attribution ? <p className="mt-3 text-[11px] text-content-muted">{attribution}</p> : null}
    </>
  );
}
