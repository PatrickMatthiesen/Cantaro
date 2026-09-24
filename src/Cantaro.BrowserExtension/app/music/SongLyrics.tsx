import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { useEffect, useRef, useState } from 'react';
import type { LyricsResult } from './musicLyrics';
import { LyricsResultContent } from './LyricsResultContent';
import { loadSongLyrics } from './musicService';
import { SongSection } from './SongSection';

type LyricsView =
  | { kind: 'loading' }
  | { kind: 'ready'; result: LyricsResult }
  | { kind: 'error'; message: string }
  | { kind: 'unavailable'; message: string };

export function SongLyrics({ song }: { song: MusicLibrarySong }) {
  const [view, setView] = useState<LyricsView>({ kind: 'loading' });
  const requestId = useRef(0);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    const controller = new AbortController();
    setView({ kind: 'loading' });
    void loadSongLyrics(song, controller.signal)
      .then((result) => {
        if (requestId.current === currentRequest) setView({ kind: 'ready', result });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || requestId.current !== currentRequest) return;
        setView({ kind: 'error', message: error instanceof Error ? error.message : 'The lyrics provider could not be reached.' });
      });
    return () => controller.abort();
  }, [song]);

  return <SongSection title="Lyrics"><LyricsContent view={view} /></SongSection>;
}

function LyricsContent({ view }: { view: LyricsView }) {
  if (view.kind === 'loading') return <LyricsLoading />;
  if (view.kind === 'ready') return <LyricsResultContent result={view.result} />;
  return <p className="text-xs text-content-muted" role="status">{view.message}</p>;
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
