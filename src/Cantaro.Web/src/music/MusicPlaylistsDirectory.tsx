import { type MusicLibraryResponse } from '@cantaro/client-shared/music';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { formatTimestamp, platformName, playlistArtwork } from './musicPresentation';

export function MusicPlaylistsDirectory({ library }: { library: MusicLibraryResponse }) {
  return (
    <MusicPageShell library={library} activeSong={library.songs[0]}>
      <section className="space-y-5">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-xs font-black tracking-[0.22em] text-violet-600 uppercase">Your library</p>
            <h1 className="mt-2 text-4xl font-black text-slate-950">Playlists</h1>
          </div>
          <span className="rounded-2xl bg-white/70 px-4 py-2 text-sm font-black text-slate-600">
            {library.summary.playlistCount.toLocaleString()} total
          </span>
        </div>

        {library.playlists.length > 0 ? (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {library.playlists.map((playlist, index) => {
              const lastSyncedAt = playlist.services
                .map((service) => service.lastSyncedAt)
                .filter((value): value is string => Boolean(value))
                .sort()
                .at(-1);

              return (
                <article key={playlist.id} className="overflow-hidden rounded-3xl bg-white/70 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur">
                  <img src={playlistArtwork(playlist, index)} alt="" className="h-40 w-full object-cover" />
                  <div className="p-5">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <h2 className="line-clamp-2 text-xl font-black text-slate-950">{playlist.name}</h2>
                        {playlist.description ? <p className="mt-2 line-clamp-2 text-sm font-medium text-slate-500">{playlist.description}</p> : null}
                      </div>
                      <span className="shrink-0 rounded-full bg-[#eeeaff] px-3 py-1 text-xs font-black text-violet-700">
                        {playlist.entryCount.toLocaleString()}
                      </span>
                    </div>
                    <div className="mt-4 flex flex-wrap gap-2">
                      {playlist.services.length > 0 ? playlist.services.map((service) => (
                        <span key={`${playlist.id}-${service.service}-${service.servicePlaylistId}`} className="rounded-full bg-slate-100 px-3 py-1 text-xs font-black text-slate-600">
                          {platformName(service.service)}
                        </span>
                      )) : (
                        <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-black text-slate-500">Cantaro</span>
                      )}
                    </div>
                    {lastSyncedAt ? <p className="mt-4 text-xs font-semibold text-slate-400">Updated {formatTimestamp(lastSyncedAt)}</p> : null}
                  </div>
                </article>
              );
            })}
          </div>
        ) : (
          <MusicEmptyPanel title="No playlists yet" detail="Your playlists will appear here once Cantaro has music to work with." />
        )}
      </section>
    </MusicPageShell>
  );
}
