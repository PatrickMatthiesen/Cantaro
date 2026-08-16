import type { ReactNode } from 'react';
import type { MusicLibraryResponse } from '@cantaro/client-shared/music';
import { useMusicLibraryContext } from './MusicLibraryStateContext';
import { MusicPageShell } from './MusicPageShell';

export function MusicLibraryPanel({ children }: { children: (library: MusicLibraryResponse) => ReactNode }) {
  const { library, error, isLoading, reload } = useMusicLibraryContext();

  if (isLoading) {
    return (
      <MusicPageShell>
        <div className="flex min-h-[360px] items-center justify-center border-y border-border-subtle">
          <div className="flex items-center gap-3 text-sm font-semibold text-content-muted">
            <span className="h-2.5 w-2.5 animate-pulse rounded-full bg-personal-accent" aria-hidden />
            Loading your library
          </div>
        </div>
      </MusicPageShell>
    );
  }

  if (error) {
    return (
      <MusicPageShell>
        <section className="border-y border-danger-border bg-danger-surface p-5 text-sm text-danger-content">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>{error}</span>
            <button type="button" className="font-semibold underline" onClick={() => void reload()}>
              Retry
            </button>
          </div>
        </section>
      </MusicPageShell>
    );
  }

  return <>{library ? children(library) : null}</>;
}
