import { Link } from '@tanstack/react-router';
import {
  MusicPlatformIcon,
  type MusicLibraryResponse,
} from '@cantaro/client-shared/music';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { formatTimestamp, isPlatformId, platformHoverClass, platformName, playlistArtwork, playlistLastSyncedAt } from './musicPresentation';

export function MusicPlaylistsDirectory({ library }: { library: MusicLibraryResponse }) {
  const syncedPlaylistCount = library.playlists.filter((playlist) => playlist.services.length > 0).length;
  const connectedPlatformCount = new Set(library.playlists.flatMap((playlist) => playlist.services.map((service) => service.service))).size;

  return (
    <MusicPageShell library={library}>
      <section className="space-y-6">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-xs font-black tracking-[0.22em] text-accent uppercase">Archive management</p>
            <h1 className="mt-2 text-4xl font-black text-content">Playlists</h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 font-semibold text-content-muted">
              Browse what Cantaro knows before deciding which playlists should stay aligned across platforms.
            </p>
          </div>
          <Link
            to="/music/platforms/sync"
            className="rounded-2xl bg-action px-5 py-3 text-sm font-black text-action-content transition hover:bg-action-hover"
          >
            Create playlist sync
          </Link>
        </div>

        <div className="grid gap-3 sm:grid-cols-3">
          <div className="rounded-[1.35rem] border border-border-subtle bg-surface-translucent p-4 shadow-[0_12px_34px_rgba(88,74,150,0.05)]">
            <p className="text-xs font-black tracking-[0.14em] text-content-muted uppercase">Known playlists</p>
            <p className="mt-2 text-2xl font-black text-content">{library.summary.playlistCount.toLocaleString()}</p>
          </div>
          <div className="rounded-[1.35rem] border border-border-subtle bg-surface-translucent p-4 shadow-[0_12px_34px_rgba(88,74,150,0.05)]">
            <p className="text-xs font-black tracking-[0.14em] text-content-muted uppercase">Mapped for sync</p>
            <p className="mt-2 text-2xl font-black text-content">{syncedPlaylistCount.toLocaleString()}</p>
          </div>
          <div className="rounded-[1.35rem] border border-border-subtle bg-surface-translucent p-4 shadow-[0_12px_34px_rgba(88,74,150,0.05)]">
            <p className="text-xs font-black tracking-[0.14em] text-content-muted uppercase">Platforms seen</p>
            <p className="mt-2 text-2xl font-black text-content">{connectedPlatformCount.toLocaleString()}</p>
          </div>
        </div>

        {library.playlists.length > 0 ? (
          <div className="grid grid-cols-[repeat(auto-fit,minmax(230px,1fr))] gap-4 2xl:grid-cols-4">
            {library.playlists.map((playlist, index) => {
              const lastSyncedAt = playlistLastSyncedAt(playlist) ?? undefined;

              return (
                <Link
                  key={playlist.id}
                  to="/music/playlists/$playlistId"
                  params={{ playlistId: playlist.id }}
                  className="music-playlist-card group overflow-hidden rounded-[1.25rem] border border-border-subtle bg-surface-translucent shadow-[0_12px_34px_rgba(88,74,150,0.05)] backdrop-blur transition hover:bg-surface focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none"
                >
                  <div className="relative h-28 overflow-hidden">
                    <img src={playlistArtwork(playlist, index)} alt="" className="h-full w-full object-cover transition group-hover:scale-[1.03]" />
                    <div className="pointer-events-none absolute inset-0 bg-linear-to-t from-slate-950/28 via-transparent to-white/10" />
                    <span className="absolute right-3 bottom-3 rounded-full bg-action/72 px-3 py-1 text-xs font-black text-action-content backdrop-blur">
                      {playlist.entryCount.toLocaleString()}
                    </span>
                  </div>
                  <div className="p-4">
                    <div className="min-w-0">
                      <h2 className="line-clamp-1 text-lg font-black text-content">{playlist.name}</h2>
                      {playlist.description ? <p className="mt-1 line-clamp-2 text-sm font-semibold text-content-muted">{playlist.description}</p> : null}
                    </div>
                    <div className="mt-3 flex min-h-7 flex-wrap gap-1.5">
                      {playlist.services.length > 0 ? playlist.services.map((service) => {
                        const servicePlatformId = isPlatformId(service.service) ? service.service : null;
                        const hoverClass = servicePlatformId
                          ? platformHoverClass(servicePlatformId)
                          : 'hover:border-border-subtle hover:bg-accent-soft hover:text-accent-strong';

                        return (
                          <span
                            key={`${playlist.id}-${service.service}-${service.servicePlaylistId}`}
                            className={`inline-flex h-7 items-center gap-1.5 rounded-full border border-border-subtle bg-surface-translucent px-2.5 text-[0.68rem] font-black text-content-muted transition ${hoverClass}`}
                          >
                            {servicePlatformId ? <MusicPlatformIcon platformId={servicePlatformId} className="h-3.5 w-3.5 shrink-0" /> : null}
                            {platformName(service.service)}
                          </span>
                        );
                      }) : (
                        <span className="rounded-full bg-surface-subtle px-3 py-1 text-xs font-black text-content-muted">Cantaro only</span>
                      )}
                    </div>
                    {lastSyncedAt ? <p className="mt-3 text-xs font-semibold text-content-subtle">Updated {formatTimestamp(lastSyncedAt)}</p> : null}
                  </div>
                </Link>
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
