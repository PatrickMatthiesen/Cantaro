import {
  MusicPlatformIcon,
  MusicUiIcon,
  type MusicLibraryPlaylist,
  type MusicLibraryResponse,
  type MusicLibrarySong,
} from '@cantaro/client-shared/music';
import { actionClassName } from '@cantaro/client-shared/ui';
import { Link } from '@tanstack/react-router';
import { MusicTrackTable, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import {
  isPlatformId,
  platformName,
  playlistArtwork,
  songArtist,
  songArtwork,
  visiblePlatformIds,
} from './musicPresentation';

function mapSongToTrack(song: MusicLibrarySong, index: number): MusicCollectionTrack {
  return {
    id: song.id,
    detailSongId: song.id,
    title: song.title,
    artist: songArtist(song),
    albums: song.albums,
    artworkUrl: songArtwork(song, index),
    durationSeconds: song.durationSeconds,
    platformIds: visiblePlatformIds(song),
  };
}

function PlaylistArtworkLink({ playlist, index }: { playlist: MusicLibraryPlaylist; index: number }) {
  return (
    <Link
      to="/music/playlists/$playlistId"
      params={{ playlistId: playlist.id }}
      className="group relative aspect-[4/3] min-w-0 overflow-hidden bg-surface-subtle focus-visible:outline-2 focus-visible:outline-focus"
    >
      <img
        src={playlistArtwork(playlist, index)}
        alt=""
        className="h-full w-full object-cover transition-transform duration-200 group-hover:scale-[1.025] motion-reduce:transition-none"
      />
      <span className="absolute inset-x-0 bottom-0 bg-linear-to-t from-black/90 via-black/65 to-transparent px-4 pt-12 pb-4 text-white">
        <strong className="block truncate text-base font-bold">{playlist.name}</strong>
        <span className="mt-1 flex items-center gap-2 text-xs text-white/78">
          {playlist.entryCount.toLocaleString()} songs
          {playlist.services.slice(0, 2).map((service) => {
            const platformId = isPlatformId(service.service) ? service.service : null;
            return platformId ? <MusicPlatformIcon key={service.servicePlaylistId} platformId={platformId} className="size-4" /> : null;
          })}
        </span>
      </span>
    </Link>
  );
}

function ArchiveSummary({ library }: { library: MusicLibraryResponse }) {
  const mappedPlaylists = library.playlists.filter((playlist) => playlist.services.length > 0);
  const platforms = new Set(mappedPlaylists.flatMap((playlist) => playlist.services.map((service) => service.service)));

  return (
    <dl className="mt-6 flex flex-wrap gap-x-8 gap-y-4 border-y border-border-subtle py-4">
      <div>
        <dt className="text-sm text-content-muted">Songs</dt>
        <dd className="mt-1 text-xl font-bold text-personal-accent-strong">{library.summary.songCount.toLocaleString()}</dd>
      </div>
      <div>
        <dt className="text-sm text-content-muted">Playlists</dt>
        <dd className="mt-1 text-xl font-bold text-content">{library.summary.playlistCount.toLocaleString()}</dd>
      </div>
      <div>
        <dt className="text-sm text-content-muted">Synced playlists</dt>
        <dd className="mt-1 text-xl font-bold text-content">{mappedPlaylists.length.toLocaleString()}</dd>
      </div>
      <div>
        <dt className="text-sm text-content-muted">Connected sources</dt>
        <dd className="mt-1 text-xl font-bold text-content">{platforms.size.toLocaleString()}</dd>
      </div>
    </dl>
  );
}

function PlaylistStrip({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  if (playlists.length === 0) return null;
  return (
    <section className="border-b border-border-subtle py-8" aria-labelledby="recent-playlists-title">
      <div className="flex items-end justify-between gap-4">
        <div>
          <h2 id="recent-playlists-title" className="text-xl font-bold text-content">Playlists in your archive</h2>
          <p className="mt-1 text-sm text-content-muted">Open a playlist to inspect its canonical songs and platform mappings.</p>
        </div>
        <Link to="/music/playlists" className="text-sm font-semibold text-content-muted hover:text-personal-accent-strong">View all</Link>
      </div>
      <div className="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {playlists.slice(0, 5).map((playlist, index) => <PlaylistArtworkLink key={playlist.id} playlist={playlist} index={index} />)}
      </div>
    </section>
  );
}

export function MusicHomeDashboard({ library }: { library: MusicLibraryResponse }) {
  const tracks = library.songs.map(mapSongToTrack);

  return (
    <MusicPageShell library={library}>
      <div className="mx-auto max-w-360">
        <header className="flex flex-wrap items-end justify-between gap-5 border-b border-border-subtle pb-7">
          <div>
            <h1 className="text-3xl font-black tracking-[-0.03em] text-content sm:text-4xl">Songs</h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-content-muted">
              The canonical recordings Cantaro knows across your connected music services.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Link to="/music/matching" className={actionClassName({ tone: 'secondary' })}>
              <MusicUiIcon name="sparkles" className="size-4" /> Review matching
            </Link>
            <Link to="/music/platforms/sync" className={actionClassName({ tone: 'personal' })}>
              <MusicUiIcon name="refresh" className="size-4" /> Add playlist sync
            </Link>
          </div>
        </header>

        <ArchiveSummary library={library} />
        <PlaylistStrip playlists={library.playlists} />

        <section className="py-8" aria-labelledby="song-library-title">
          <div className="mb-5 flex items-end justify-between gap-4">
            <div>
              <h2 id="song-library-title" className="text-xl font-bold text-content">All songs</h2>
              <p className="mt-1 text-sm text-content-muted">Open a song for sources, lyrics, playlist appearances, and archive metadata.</p>
            </div>
          </div>
          {tracks.length > 0 ? (
            <MusicTrackTable tracks={tracks} emptyTrackLabel="No songs found" />
          ) : (
            <MusicEmptyPanel title="No songs yet" detail={`Connect a platform or import a playlist to begin building Cantaro's canonical music archive.`} />
          )}
        </section>

        {library.playlists.some((playlist) => playlist.services.length > 0) ? (
          <p className="border-t border-border-subtle py-5 text-sm text-content-muted">
            Playlist mappings currently include {Array.from(new Set(library.playlists.flatMap((playlist) => playlist.services.map((service) => platformName(service.service))))).join(', ')}.
          </p>
        ) : null}
      </div>
    </MusicPageShell>
  );
}
