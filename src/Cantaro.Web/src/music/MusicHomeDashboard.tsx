import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import {
  MusicPlatformIcon,
  MusicUiIcon,
  platformCatalog,
  type MusicLibraryPlaylist,
  type MusicLibraryResponse,
  type MusicLibrarySong,
  type PlatformId,
} from '@cantaro/client-shared/music';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicTrackTable, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicPageShell } from './MusicPageShell';
import { MusicUpNextPanel } from './MusicUpNextPanel';
import { useMusicQueue } from './useMusicQueue';
import {
  formatDuration,
  formatRelativeTime,
  latestTimestamp,
  platformHoverClass,
  platformName,
  playlistArtwork,
  playlistGradients,
  playlistLastSyncedAt,
  songArtist,
  songArtwork,
  visiblePlatformIds,
} from './musicPresentation';

const discoveryTiles = [
  { label: 'Recently added', detail: 'Newest arrivals', tint: 'hover:border-sky-300 hover:bg-sky-50/80 hover:text-sky-900' },
  { label: 'Large playlists', detail: 'The monoliths', tint: 'hover:border-violet-300 hover:bg-violet-50/80 hover:text-violet-900' },
  { label: 'Needs coverage', detail: 'Sparse platforms', tint: 'hover:border-amber-300 hover:bg-amber-50/80 hover:text-amber-900' },
  { label: 'Deep cuts', detail: 'Older saves', tint: 'hover:border-rose-300 hover:bg-rose-50/80 hover:text-rose-900' },
];

const implementedPlatformIds = platformCatalog
  .filter((platform) => platform.implemented)
  .map((platform) => platform.id);

function playlistHasSyncProblem(playlist: MusicLibraryPlaylist): boolean {
  return playlist.services.some((service) => service.lastSyncStatus === 'partial_failure' || service.lastSyncStatus === 'error');
}

function getArchiveStats(library: MusicLibraryResponse) {
  const syncedPlaylists = library.playlists.filter((playlist) => playlist.services.length > 0);
  const serviceMappings = syncedPlaylists.flatMap((playlist) => playlist.services);
  const lastSync = latestTimestamp(serviceMappings.map((service) => service.lastSyncedAt));
  const problemCount = syncedPlaylists.filter(playlistHasSyncProblem).length;
  const unsyncedPlaylists = Math.max(library.summary.playlistCount - syncedPlaylists.length, 0);

  return {
    syncedPlaylists,
    serviceMappings,
    lastSync,
    problemCount,
    unsyncedPlaylists,
  };
}

function ArchiveMetric({
  label,
  value,
  detail,
  tone = 'slate',
}: {
  label: string;
  value: string;
  detail: string;
  tone?: 'slate' | 'violet' | 'emerald' | 'amber';
}) {
  const toneClass = {
    slate: 'border-slate-200 bg-white/72 text-slate-700',
    violet: 'border-violet-200 bg-violet-50/80 text-violet-800',
    emerald: 'border-emerald-200 bg-emerald-50/80 text-emerald-800',
    amber: 'border-amber-200 bg-amber-50/80 text-amber-800',
  }[tone];

  return (
    <div className={`music-archive-metric music-archive-metric--${tone} rounded-2xl border px-3.5 py-2.5 ${toneClass}`}>
      <p className="text-[0.62rem] font-black tracking-[0.13em] uppercase opacity-75">{label}</p>
      <p className="mt-1 text-lg font-black text-slate-950">{value}</p>
      <p className="mt-0.5 text-[0.68rem] leading-4 font-semibold opacity-80">{detail}</p>
    </div>
  );
}

function PlatformChipContent({ platformId }: { platformId: PlatformId }) {
  const platform = platformCatalog.find((item) => item.id === platformId);

  return (
    <>
      <MusicPlatformIcon platformId={platform?.iconId ?? platformId} className="h-5 w-5" />
      <span className="hidden sm:inline">{platform?.name ?? platformId}</span>
    </>
  );
}

function PlatformCoverageChip({ platformId, isMapped }: { platformId: PlatformId; isMapped: boolean }) {
  if (!isMapped) {
    return (
      <span
        className="inline-flex h-8 items-center gap-1.5 rounded-full border border-dashed border-slate-200 bg-white/46 px-2.5 text-[0.72rem] font-black text-slate-400"
        title={`${platformName(platformId)} is not mapped yet`}
      >
        <PlatformChipContent platformId={platformId} />
      </span>
    );
  }

  return (
    <span
      className={`inline-flex h-8 items-center gap-1.5 rounded-full border border-white bg-white px-2.5 text-[0.72rem] font-black text-slate-800 shadow-sm transition ${platformHoverClass(platformId, 'hover:border-violet-300 hover:bg-violet-50 hover:text-violet-700')}`}
      title={`${platformName(platformId)} is mapped`}
    >
      <PlatformChipContent platformId={platformId} />
    </span>
  );
}

function getArchiveHealth(stats: ReturnType<typeof getArchiveStats>) {
  if (stats.problemCount > 0) {
    return {
      tone: 'amber' as const,
      label: 'Needs review',
      detail: `${stats.problemCount.toLocaleString()} playlist${stats.problemCount === 1 ? '' : 's'} need attention`,
    };
  }

  if (stats.syncedPlaylists.length > 0) {
    return {
      tone: 'emerald' as const,
      label: 'Ready',
      detail: 'No failed playlist mappings found',
    };
  }

  return {
    tone: 'violet' as const,
    label: 'Start syncing',
    detail: 'Choose a source playlist to create the first mapping',
  };
}

function getLastSyncDetail(unsyncedPlaylists: number): string {
  return unsyncedPlaylists > 0
    ? `${unsyncedPlaylists.toLocaleString()} playlist${unsyncedPlaylists === 1 ? '' : 's'} only in Cantaro`
    : 'Every playlist has a platform mapping';
}

function ArchiveCommandPanel({ library }: { library: MusicLibraryResponse }) {
  const stats = getArchiveStats(library);
  const health = getArchiveHealth(stats);

  return (
    <section className="music-archive-panel relative overflow-hidden rounded-[1.75rem] border border-white/80 bg-white/76 p-5 shadow-[0_18px_54px_rgba(88,74,150,0.08)] backdrop-blur-xl sm:p-6">
      <div className="pointer-events-none absolute inset-x-0 top-0 h-1 bg-linear-to-r from-violet-500 via-slate-950 to-emerald-500" aria-hidden />
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_300px] xl:items-start">
        <div className="min-w-0">
          <p className="text-[0.68rem] font-black tracking-[0.16em] text-violet-700 uppercase">Archive command</p>
          <h1 className="mt-2 max-w-2xl text-3xl leading-tight font-black tracking-[-0.03em] text-slate-950 sm:text-4xl">
            Know what is synced before you send it anywhere.
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 font-semibold text-slate-600 sm:text-base sm:leading-7">
            Cantaro keeps the canonical playlist copy here, then tracks which platforms have a mapped version and where gaps need review.
          </p>

          <div className="mt-5 flex flex-wrap gap-3">
            <Link
              to="/music/platforms"
              className="inline-flex h-11 items-center gap-2 rounded-2xl bg-slate-950 px-4 text-sm font-black text-white transition hover:bg-violet-700 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 focus-visible:outline-none"
            >
              <MusicUiIcon name="shieldCheck" className="h-5 w-5" />
              Review sync health
            </Link>
            <Link
              to="/music/platforms/sync"
              className="inline-flex h-11 items-center gap-2 rounded-2xl border border-violet-200 bg-white px-4 text-sm font-black text-violet-700 transition hover:border-violet-300 hover:bg-violet-50 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 focus-visible:outline-none"
            >
              <MusicUiIcon name="refresh" className="h-5 w-5" />
              Add playlist sync
            </Link>
            <Link
              to="/music/playlists"
              className="inline-flex h-11 items-center gap-2 rounded-2xl border border-slate-200 bg-white/72 px-4 text-sm font-black text-slate-700 transition hover:border-sky-300 hover:bg-sky-50 hover:text-sky-800 focus-visible:ring-2 focus-visible:ring-sky-400 focus-visible:ring-offset-2 focus-visible:outline-none"
            >
              <MusicUiIcon name="listMusic" className="h-5 w-5" />
              Browse playlists
            </Link>
          </div>
        </div>

        <div className="grid gap-2 sm:grid-cols-3 xl:grid-cols-1">
          <ArchiveMetric
            label="Sync health"
            value={health.label}
            detail={health.detail}
            tone={health.tone}
          />
          <ArchiveMetric
            label="Mapped playlists"
            value={`${stats.syncedPlaylists.length.toLocaleString()} / ${library.summary.playlistCount.toLocaleString()}`}
            detail={`${stats.serviceMappings.length.toLocaleString()} platform mapping${stats.serviceMappings.length === 1 ? '' : 's'}`}
            tone="violet"
          />
          <ArchiveMetric
            label="Last known sync"
            value={formatRelativeTime(stats.lastSync)}
            detail={getLastSyncDetail(stats.unsyncedPlaylists)}
          />
        </div>
      </div>
    </section>
  );
}

function PlaylistCoveragePanel({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  const visiblePlaylists = playlists
    .slice()
    .sort((left, right) => {
      const leftProblem = playlistHasSyncProblem(left) ? 1 : 0;
      const rightProblem = playlistHasSyncProblem(right) ? 1 : 0;
      if (leftProblem !== rightProblem) return rightProblem - leftProblem;
      return right.services.length - left.services.length;
    })
    .slice(0, 5);

  return (
    <section className="music-panel rounded-[1.75rem] border border-white/80 bg-white/62 p-4 shadow-[0_16px_48px_rgba(88,74,150,0.06)] backdrop-blur">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-xl font-black text-slate-950">Playlist coverage</h2>
          <p className="mt-1 text-sm font-semibold text-slate-500">See which playlists already have platform mappings before syncing more.</p>
        </div>
        <Link to="/music/playlists" className="rounded-2xl bg-white px-4 py-2 text-sm font-black text-violet-700 transition hover:bg-violet-50">View all</Link>
      </div>

      {visiblePlaylists.length > 0 ? (
        <div className="mt-4 space-y-3">
          {visiblePlaylists.map((playlist, index) => (
            <Link
              key={playlist.id}
              to="/music/playlists/$playlistId"
              params={{ playlistId: playlist.id }}
              className="group grid gap-3 rounded-2xl border border-white/70 bg-white/66 p-2.5 transition hover:border-violet-200 hover:bg-white focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none md:grid-cols-[56px_minmax(0,1fr)_auto]"
            >
              <img src={playlistArtwork(playlist, index)} alt="" className="h-14 w-14 rounded-xl object-cover shadow-[0_10px_24px_rgba(15,23,42,0.12)]" />
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h3 className="min-w-0 truncate text-base font-black text-slate-950">{playlist.name}</h3>
                  {playlistHasSyncProblem(playlist) ? (
                    <span className="rounded-full bg-amber-50 px-2.5 py-1 text-xs font-black text-amber-700">Review</span>
                  ) : null}
                </div>
                <p className="mt-1 text-sm font-semibold text-slate-500">
                  {playlist.entryCount.toLocaleString()} songs · {formatRelativeTime(playlistLastSyncedAt(playlist))}
                </p>
                <div className="mt-2 flex flex-wrap gap-1.5 md:hidden">
                  {implementedPlatformIds.map((platformId) => (
                    <PlatformCoverageChip
                      key={`${playlist.id}-${platformId}`}
                      platformId={platformId}
                      isMapped={playlist.services.some((service) => service.service === platformId)}
                    />
                  ))}
                </div>
              </div>
              <div className="hidden items-center gap-2 self-center md:flex">
                {implementedPlatformIds.map((platformId) => (
                  <PlatformCoverageChip
                    key={`${playlist.id}-${platformId}-desktop`}
                    platformId={platformId}
                    isMapped={playlist.services.some((service) => service.service === platformId)}
                  />
                ))}
                <span className="rounded-full bg-[#eeeaff] px-3 py-1.5 text-xs font-black text-violet-700">
                  {playlist.services.length.toLocaleString()} mapped
                </span>
              </div>
            </Link>
          ))}
        </div>
      ) : (
        <div className="mt-4">
          <MusicEmptyPanel title="No playlists yet" detail="Connect a platform to import playlists into Cantaro's archive." />
        </div>
      )}
    </section>
  );
}

function PlaylistBrowser({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  const visiblePlaylists = playlists.slice(0, 6);

  return (
    <section>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-slate-950">Browse the archive</h2>
          <p className="mt-1 text-sm font-semibold text-slate-500">Open playlists to inspect the actual songs before choosing sync targets.</p>
        </div>
        <Link to="/music/playlists" className="text-sm font-black text-violet-600 transition hover:text-violet-500">View all</Link>
      </div>

      {visiblePlaylists.length > 0 ? (
        <div className="grid grid-cols-[repeat(auto-fit,minmax(170px,1fr))] gap-4">
          {visiblePlaylists.map((playlist, index) => (
            <Link
              key={playlist.id}
              to="/music/playlists/$playlistId"
              params={{ playlistId: playlist.id }}
              className="group overflow-hidden rounded-2xl border border-white/80 bg-white/64 shadow-[0_12px_34px_rgba(88,74,150,0.07)] transition hover:-translate-y-0.5 hover:border-violet-200 hover:bg-white focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 focus-visible:outline-none"
            >
              <div className={`relative h-24 bg-linear-to-br ${playlistGradients[index % playlistGradients.length]}`}>
                <img src={playlistArtwork(playlist, index)} alt="" className="h-full w-full object-cover opacity-70 transition group-hover:scale-[1.03]" />
                <div className="absolute inset-0 bg-linear-to-t from-slate-950/62 to-transparent" />
              </div>
              <div className="p-4">
                <h3 className="line-clamp-2 text-sm font-black text-slate-950">{playlist.name}</h3>
                <p className="mt-2 text-xs font-semibold text-slate-500">{playlist.entryCount.toLocaleString()} songs</p>
              </div>
            </Link>
          ))}
        </div>
      ) : (
        <MusicEmptyPanel title="No playlists yet" detail="Your playlists will settle in here once Cantaro has music to work with." />
      )}
    </section>
  );
}

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

function SongTable({
  tracks,
  onPlayTrack,
  onQueueTrack,
  query,
}: {
  tracks: MusicCollectionTrack[];
  query: string;
  onPlayTrack: (track: MusicCollectionTrack) => void;
  onQueueTrack: (track: MusicCollectionTrack) => void;
}) {
  return (
    <section className="music-panel rounded-[1.75rem] border border-white/80 bg-white/62 p-4 shadow-[0_16px_48px_rgba(88,74,150,0.06)] backdrop-blur">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-slate-950">{query.trim() ? 'Matching songs' : 'Songs in the archive'}</h2>
          <p className="mt-1 text-sm font-semibold text-slate-500">
            {query.trim() ? 'Filtered by the global music search.' : 'Use the top search to inspect songs, artists, or source playlists.'}
          </p>
        </div>
        <Link to="/music/songs" className="rounded-2xl bg-white px-4 py-2 text-sm font-black text-violet-700 transition hover:bg-violet-50">Open songs</Link>
      </div>

      <MusicTrackTable
        tracks={tracks.slice(0, 10)}
        emptyTrackLabel="No songs found"
        emptyTrackDetail="Try a different search."
        onPlayTrack={onPlayTrack}
        onQueueTrack={onQueueTrack}
      />
    </section>
  );
}

function RediscoverySongPanel({
  title,
  detail,
  songs,
  ranked = false,
}: {
  title: string;
  detail: string;
  songs: MusicLibrarySong[];
  ranked?: boolean;
}) {
  return (
    <section className="music-panel rounded-[1.5rem] border border-white/80 bg-white/58 p-4 shadow-[0_12px_34px_rgba(88,74,150,0.05)] backdrop-blur">
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-base font-black text-slate-950">{title}</h2>
          <p className="mt-1 text-xs font-semibold text-slate-500">{detail}</p>
        </div>
        <Link to="/music/songs" className="text-xs font-black text-violet-600 transition hover:text-violet-500">View all</Link>
      </div>
      <div className="space-y-3">
        {songs.length > 0 ? songs.map((song, index) => (
          <Link
            key={song.id}
            to="/music/songs/$songId"
            params={{ songId: song.id }}
            className={`group -mx-2 grid items-center gap-3 rounded-xl px-2 py-1.5 transition hover:bg-white/80 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none ${ranked ? 'grid-cols-[20px_44px_minmax(0,1fr)_auto]' : 'grid-cols-[44px_minmax(0,1fr)_auto]'}`}
          >
            {ranked ? <span className="text-sm font-black text-slate-500">{index + 1}</span> : null}
            <img src={songArtwork(song, index)} alt="" className="h-11 w-11 rounded-xl object-cover transition group-hover:ring-2 group-hover:ring-violet-300" />
            <div className="min-w-0">
              <p className="truncate text-sm font-black text-slate-950">{song.title}</p>
              <p className="truncate text-xs font-semibold text-slate-500">{songArtist(song)}</p>
            </div>
            <span className="font-mono text-xs text-slate-500">{formatDuration(song.durationSeconds)}</span>
          </Link>
        )) : (
          <p className="text-sm font-medium text-slate-500">Nothing here yet</p>
        )}
      </div>
    </section>
  );
}

function RediscoveryPanel() {
  return (
    <section className="music-panel rounded-[1.5rem] border border-white/80 bg-white/58 p-4 shadow-[0_12px_34px_rgba(88,74,150,0.05)] backdrop-blur">
      <div className="mb-3 flex items-center justify-between gap-3">
        <div>
          <h2 className="text-base font-black text-slate-950">Rediscovery</h2>
          <p className="mt-1 text-xs font-semibold text-slate-500">Small ways back into the archive.</p>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-2">
        {discoveryTiles.map((tile) => (
          <Link
            key={tile.label}
            to="/music/songs"
            className={`rounded-2xl border border-slate-200/70 bg-white/64 p-3 text-left text-slate-700 transition ${tile.tint} focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none`}
          >
            <span className="block text-sm font-black">{tile.label}</span>
            <span className="mt-1 block text-xs font-semibold opacity-75">{tile.detail}</span>
          </Link>
        ))}
      </div>
    </section>
  );
}

function useMusicHomePlayback(songs: MusicLibrarySong[], filteredSongs: MusicLibrarySong[]) {
  const allTracks = songs.map(mapSongToTrack);
  const queue = useMusicQueue(allTracks);
  const activeSong = songs.find((song) => song.id === queue.activeTrackId);

  return {
    activeSong,
    activeTrack: queue.activeTrack,
    queuedTracks: queue.queuedTracks,
    tracks: filteredSongs.map((song, index) => ({
      ...mapSongToTrack(song, index),
      isPlaying: song.id === queue.activeTrackId,
    })),
    playTrack: queue.playTrack,
    playSong: (song: MusicLibrarySong) => {
      const track = allTracks.find((candidate) => candidate.id === song.id);
      if (track) queue.playTrack(track);
    },
    queueTrack: queue.queueTrack,
    clearQueue: queue.clearQueue,
    stopTrack: queue.stop,
  };
}

export function MusicHomeDashboard({ library }: { library: MusicLibraryResponse }) {
  const [query, setQuery] = useState('');
  const songs = library.songs;
  const filteredSongs = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    if (!normalizedQuery) return songs;

    return songs.filter((song) => {
      const haystack = [song.title, song.artist, ...song.playlists.map((playlist) => playlist.playlistName)]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return haystack.includes(normalizedQuery);
    });
  }, [query, songs]);
  const playback = useMusicHomePlayback(songs, filteredSongs);

  return (
    <MusicPageShell
      library={library}
      activeSong={playback.activeSong}
      onStopActiveSong={playback.stopTrack}
      searchValue={query}
      onSearchChange={setQuery}
    >
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_300px] 2xl:grid-cols-[minmax(0,1fr)_340px]">
        <div className="min-w-0 space-y-6">
          <ArchiveCommandPanel library={library} />
          <PlaylistCoveragePanel playlists={library.playlists} />
          <PlaylistBrowser playlists={library.playlists} />
          <SongTable
            tracks={playback.tracks}
            query={query}
            onPlayTrack={playback.playTrack}
            onQueueTrack={playback.queueTrack}
          />
        </div>

        <aside className="space-y-5">
          <MusicUpNextPanel
            activeTrack={playback.activeTrack}
            queuedTracks={playback.queuedTracks}
            onClearQueue={playback.clearQueue}
          />
          <RediscoverySongPanel title="Recently added" detail="Fresh imports to inspect" songs={songs.slice(0, 5)} />
          <RediscoverySongPanel title="Deep cuts" detail="Older saves worth checking" songs={[...songs].reverse().slice(0, 5)} ranked />
          <RediscoveryPanel />
        </aside>
      </div>
    </MusicPageShell>
  );
}
