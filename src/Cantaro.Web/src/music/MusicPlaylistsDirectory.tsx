import { MusicPlatformIcon, MusicUiIcon, type MusicLibraryResponse } from '@cantaro/client-shared/music';
import { actionClassName } from '@cantaro/client-shared/ui';
import { Link } from '@tanstack/react-router';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { formatTimestamp, isPlatformId, platformName, playlistArtwork, playlistLastSyncedAt } from './musicPresentation';

export function MusicPlaylistsDirectory({ library }: { library: MusicLibraryResponse }) {
  const syncedPlaylistCount = library.playlists.filter((playlist) => playlist.services.length > 0).length;
  const connectedPlatformCount = new Set(library.playlists.flatMap((playlist) => playlist.services.map((service) => service.service))).size;

  return (
    <MusicPageShell library={library}>
      <div className="mx-auto max-w-360">
        <header className="flex flex-wrap items-end justify-between gap-5 border-b border-border-subtle pb-7">
          <div>
            <h1 className="text-3xl font-black tracking-[-0.03em] text-content sm:text-4xl">Playlists</h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-content-muted">
              Canonical collections and the platform copies Cantaro keeps aligned.
            </p>
          </div>
          <Link to="/music/platforms/sync" className={actionClassName({ tone: 'personal' })}>
            <MusicUiIcon name="refresh" className="size-4" /> Create playlist sync
          </Link>
        </header>

        <dl className="flex flex-wrap gap-x-8 gap-y-4 border-b border-border-subtle py-5">
          <div><dt className="text-sm text-content-muted">Known playlists</dt><dd className="mt-1 text-xl font-bold text-personal-accent-strong">{library.summary.playlistCount.toLocaleString()}</dd></div>
          <div><dt className="text-sm text-content-muted">Mapped for sync</dt><dd className="mt-1 text-xl font-bold text-content">{syncedPlaylistCount.toLocaleString()}</dd></div>
          <div><dt className="text-sm text-content-muted">Platforms seen</dt><dd className="mt-1 text-xl font-bold text-content">{connectedPlatformCount.toLocaleString()}</dd></div>
        </dl>

        {library.playlists.length > 0 ? (
          <ul className="grid grid-cols-2 gap-x-4 gap-y-7 py-7 sm:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-5">
            {library.playlists.map((playlist, index) => {
              const lastSyncedAt = playlistLastSyncedAt(playlist) ?? undefined;
              return (
                <li key={playlist.id} className="min-w-0">
                  <Link
                    to="/music/playlists/$playlistId"
                    params={{ playlistId: playlist.id }}
                    className="group block focus-visible:outline-2 focus-visible:outline-focus"
                  >
                    <span className="relative block aspect-square overflow-hidden bg-surface-subtle">
                      <img src={playlistArtwork(playlist, index)} alt="" className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transition-none" />
                      <span className="absolute inset-x-0 bottom-0 bg-linear-to-t from-black/90 via-black/62 to-transparent px-3 pt-10 pb-3 text-white">
                        <strong className="block truncate text-base font-bold">{playlist.name}</strong>
                        <span className="mt-1 block text-xs text-white/78">{playlist.entryCount.toLocaleString()} songs</span>
                      </span>
                    </span>
                    <span className="mt-3 flex min-h-6 flex-wrap items-center gap-2 text-xs text-content-muted">
                      {playlist.services.length > 0 ? playlist.services.slice(0, 3).map((service) => {
                        const platformId = isPlatformId(service.service) ? service.service : null;
                        return (
                          <span key={`${service.service}-${service.servicePlaylistId}`} className="inline-flex items-center gap-1.5" title={platformName(service.service)}>
                            {platformId ? <MusicPlatformIcon platformId={platformId} className="size-4" /> : null}
                            <span className="sr-only">{platformName(service.service)}</span>
                          </span>
                        );
                      }) : <span>Cantaro only</span>}
                      {lastSyncedAt ? <span className="ml-auto truncate">{formatTimestamp(lastSyncedAt)}</span> : null}
                    </span>
                  </Link>
                </li>
              );
            })}
          </ul>
        ) : (
          <div className="py-8"><MusicEmptyPanel title="No playlists yet" detail="Your playlists will appear here once Cantaro has music to work with." /></div>
        )}
      </div>
    </MusicPageShell>
  );
}
