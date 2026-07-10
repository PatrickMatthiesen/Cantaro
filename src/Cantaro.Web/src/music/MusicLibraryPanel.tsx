import type { ReactNode } from 'react';
import type { MusicLibraryResponse } from '@cantaro/client-shared/music';
import { GlassCard } from '@cantaro/client-shared/ui';
import { useMusicLibraryContext } from './MusicLibraryStateContext';
import { MusicPageShell } from './MusicPageShell';

export function MusicLibraryPanel({ children }: { children: (library: MusicLibraryResponse) => ReactNode }) {
  const { library, error, isLoading, reload } = useMusicLibraryContext();

  if (isLoading) {
    return (
      <MusicPageShell>
        <div className="flex min-h-[360px] items-center justify-center rounded-[1.5rem] border border-white/80 bg-white/65 shadow-[0_12px_34px_rgba(82,70,140,0.06)] backdrop-blur">
          <div className="flex items-center gap-3 text-sm font-semibold text-slate-600">
            <span className="h-2.5 w-2.5 animate-pulse rounded-full bg-violet-500" aria-hidden />
            Loading your library
          </div>
        </div>
      </MusicPageShell>
    );
  }

  if (error) {
    return (
      <MusicPageShell>
        <GlassCard className="border-rose-200 bg-rose-50 p-5 text-sm text-rose-700">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>{error}</span>
            <button type="button" className="font-semibold text-rose-800 underline" onClick={() => void reload()}>
              Retry
            </button>
          </div>
        </GlassCard>
      </MusicPageShell>
    );
  }

  return <>{library ? children(library) : null}</>;
}
