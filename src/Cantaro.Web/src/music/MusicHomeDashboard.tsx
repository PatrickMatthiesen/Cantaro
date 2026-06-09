import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { platformCatalog, type MusicLibraryPlaylist, type MusicLibraryResponse, type MusicLibrarySong } from '@cantaro/client-shared/music';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import {
  fallbackArtwork,
  formatDuration,
  playlistArtwork,
  playlistGradients,
  songArtist,
  songArtwork,
  visiblePlatformNames,
} from './musicPresentation';

const moodTiles = [
  { label: 'Bright', className: 'from-rose-300 to-orange-300' },
  { label: 'Late night', className: 'from-indigo-400 to-slate-800' },
  { label: 'Soft focus', className: 'from-emerald-300 to-cyan-500' },
  { label: 'Loud', className: 'from-fuchsia-400 to-red-500' },
  { label: 'Rainy', className: 'from-sky-300 to-violet-500' },
  { label: 'Golden', className: 'from-amber-300 to-yellow-500' },
];

function heroArtwork(featuredSong: MusicLibrarySong | null): string {
  return featuredSong ? songArtwork(featuredSong) : fallbackArtwork[0];
}

function heroPlaylistSubtitle(library: MusicLibraryResponse, playlist: MusicLibraryPlaylist): string {
  const otherSongs = Math.max(library.summary.songCount - playlist.entryCount, 0).toLocaleString();
  return `${playlist.entryCount.toLocaleString()} songs ready here, with ${otherSongs} more across your library.`;
}

function heroSongSubtitle(library: MusicLibraryResponse, featuredSong: MusicLibrarySong): string {
  const otherSongs = Math.max(library.summary.songCount - 1, 0).toLocaleString();
  return `${songArtist(featuredSong)} is ready alongside ${otherSongs} more songs.`;
}

function getHeroContent(library: MusicLibraryResponse, featuredSong: MusicLibrarySong | null) {
  const firstPlaylist = library.playlists[0] ?? null;

  return {
    artwork: heroArtwork(featuredSong),
    title: heroTitle(firstPlaylist, featuredSong),
    subtitle: heroSubtitle(library, firstPlaylist, featuredSong),
  };
}

function heroTitle(firstPlaylist: MusicLibraryPlaylist | null, featuredSong: MusicLibrarySong | null): string {
  return firstPlaylist?.name || featuredSong?.title || 'Your music library';
}

function heroSubtitle(
  library: MusicLibraryResponse,
  firstPlaylist: MusicLibraryPlaylist | null,
  featuredSong: MusicLibrarySong | null,
): string {
  if (firstPlaylist) return heroPlaylistSubtitle(library, firstPlaylist);
  if (featuredSong) return heroSongSubtitle(library, featuredSong);
  return 'Add songs and playlists to start building your personal listening home.';
}

function HeroPanel({
  library,
  featuredSong,
}: {
  library: MusicLibraryResponse;
  featuredSong: MusicLibrarySong | null;
}) {
  const hero = getHeroContent(library, featuredSong);

  return (
    <section className="relative overflow-hidden rounded-3xl bg-[#7c6ae5] p-5 text-white shadow-[0_28px_90px_rgba(89,75,180,0.22)]">
      <div className="absolute inset-0">
        <img src={hero.artwork} alt="" className="h-full w-full object-cover opacity-45" />
        <div className="absolute inset-0 bg-linear-to-r from-[#271d66]/95 via-[#5f54c8]/76 to-[#f5a7ba]/22" />
      </div>
      <div className="relative grid gap-6 md:grid-cols-[190px_1fr]">
        <img src={hero.artwork} alt="" className="aspect-square w-full max-w-[190px] rounded-3xl border border-white/45 object-cover shadow-[0_24px_70px_rgba(15,23,42,0.28)]" />
        <div className="flex min-h-[190px] flex-col justify-center">
          <p className="text-xs font-black tracking-[0.22em] text-white/78 uppercase">From your library</p>
          <h1 className="mt-3 max-w-2xl text-4xl leading-tight font-black sm:text-5xl">{hero.title}</h1>
          <p className="mt-3 max-w-xl text-base leading-7 font-medium text-white/88">{hero.subtitle}</p>
          <div className="mt-6 flex flex-wrap gap-3">
            <button type="button" className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white shadow-[0_14px_30px_rgba(15,23,42,0.22)]">
              Play
            </button>
            <Link to="/music/playlists" className="rounded-2xl bg-white/18 px-5 py-3 text-sm font-black text-white backdrop-blur transition hover:bg-white/25">
              View playlist
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}

function PlaylistStrip({ playlists }: { playlists: MusicLibraryPlaylist[] }) {
  const visiblePlaylists = playlists.slice(0, 8);

  return (
    <section>
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="text-lg font-black text-slate-950">Library Playlists</h2>
        <Link to="/music/playlists" className="text-sm font-black text-violet-600">View all</Link>
      </div>

      {visiblePlaylists.length > 0 ? (
        <div className="grid grid-cols-[repeat(auto-fit,minmax(150px,1fr))] gap-4">
          {visiblePlaylists.map((playlist, index) => (
            <article key={playlist.id} className={`group relative min-h-[160px] overflow-hidden rounded-3xl bg-linear-to-br ${playlistGradients[index % playlistGradients.length]} p-4 text-white shadow-[0_18px_44px_rgba(88,74,150,0.12)]`}>
              <img src={playlistArtwork(playlist, index)} alt="" className="absolute inset-0 h-full w-full object-cover opacity-45 transition group-hover:scale-105 group-hover:opacity-60" />
              <div className="absolute inset-0 bg-linear-to-t from-slate-950/82 via-slate-950/22 to-transparent" />
              <div className="relative flex h-full flex-col justify-end">
                <h3 className="line-clamp-2 text-sm font-black">{playlist.name}</h3>
                <p className="mt-1 text-xs font-semibold text-white/75">{playlist.entryCount.toLocaleString()} songs</p>
                <button type="button" className="absolute right-0 bottom-0 flex h-9 w-9 items-center justify-center rounded-full bg-white text-slate-950 shadow-lg" aria-label={`Play ${playlist.name}`}>
                  &gt;
                </button>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <MusicEmptyPanel title="No playlists yet" detail="Your playlists will settle in here once Cantaro has music to work with." />
      )}
    </section>
  );
}

function SongTable({
  songs,
  query,
  onQueryChange,
}: {
  songs: MusicLibrarySong[];
  query: string;
  onQueryChange: (value: string) => void;
}) {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-black text-slate-950">Today's Mixtape</h2>
        <div className="flex flex-wrap items-center gap-2">
          <input
            className="h-10 rounded-2xl border border-[#e3def8] bg-white/80 px-4 text-sm font-medium outline-none placeholder:text-slate-400 focus:border-violet-300"
            placeholder="Filter songs"
            type="search"
            value={query}
            onChange={(event) => onQueryChange(event.target.value)}
          />
          {['All', 'Songs', 'Albums'].map((filter) => (
            <button
              key={filter}
              type="button"
              className={`rounded-2xl px-4 py-2 text-xs font-black ${filter === 'All' ? 'bg-slate-950 text-white' : 'border border-[#e3def8] bg-white/70 text-slate-700'}`}
            >
              {filter}
            </button>
          ))}
        </div>
      </div>

      {songs.length > 0 ? (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] border-collapse">
            <thead>
              <tr className="text-left text-xs font-black tracking-[0.14em] text-slate-500 uppercase">
                <th className="w-12 px-3 py-3">#</th>
                <th className="px-3 py-3">Title</th>
                <th className="px-3 py-3">Artist</th>
                <th className="px-3 py-3">Available</th>
                <th className="px-3 py-3 text-right">Time</th>
                <th className="w-12 px-3 py-3" />
              </tr>
            </thead>
            <tbody>
              {songs.slice(0, 10).map((song, index) => (
                <tr key={song.id} className="group border-t border-[#ece8fb] text-sm text-slate-700 transition hover:bg-white/70">
                  <td className="px-3 py-3 font-mono text-slate-500">{index + 1}</td>
                  <td className="px-3 py-3">
                    <div className="flex items-center gap-3">
                      <img src={songArtwork(song, index)} alt="" className="h-10 w-10 rounded-xl object-cover" />
                      <div className="min-w-0">
                        <p className="truncate font-black text-slate-950">{song.title}</p>
                      </div>
                    </div>
                  </td>
                  <td className="px-3 py-3">{songArtist(song)}</td>
                  <td className="px-3 py-3">
                    <div className="flex flex-wrap gap-1.5">
                      {visiblePlatformNames(song).slice(0, 3).map((source) => (
                        <span key={source} className="rounded-full bg-[#eeeaff] px-2.5 py-1 text-xs font-black text-violet-700">
                          {source}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="px-3 py-3 text-right font-mono">{formatDuration(song.durationSeconds)}</td>
                  <td className="px-3 py-3 text-right font-black text-slate-500">...</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <MusicEmptyPanel title="No songs found" detail="Try a different search." />
      )}
    </section>
  );
}

function CompactSongPanel({
  title,
  songs,
  ranked = false,
}: {
  title: string;
  songs: MusicLibrarySong[];
  ranked?: boolean;
}) {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-base font-black text-slate-950">{title}</h2>
        <button type="button" className="text-xs font-black text-violet-600">View all</button>
      </div>
      <div className="space-y-3">
        {songs.length > 0 ? songs.map((song, index) => (
          <div key={song.id} className="grid grid-cols-[auto_44px_1fr_auto] items-center gap-3">
            {ranked ? <span className="w-4 text-sm font-black text-slate-500">{index + 1}</span> : null}
            <img src={songArtwork(song, index)} alt="" className="h-11 w-11 rounded-xl object-cover" />
            <div className="min-w-0">
              <p className="truncate text-sm font-black text-slate-950">{song.title}</p>
              <p className="truncate text-xs font-semibold text-slate-500">{songArtist(song)}</p>
            </div>
            <span className="font-mono text-xs text-slate-500">{formatDuration(song.durationSeconds)}</span>
          </div>
        )) : (
          <p className="text-sm font-medium text-slate-500">Nothing here yet</p>
        )}
      </div>
    </section>
  );
}

function MoodPanel() {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-base font-black text-slate-950">Browse Moods</h2>
        <button type="button" className="text-xs font-black text-violet-600">View all</button>
      </div>
      <div className="grid grid-cols-2 gap-3">
        {moodTiles.map((mood) => (
          <button
            key={mood.label}
            type="button"
            className={`min-h-14 rounded-2xl bg-linear-to-br ${mood.className} px-3 text-left text-sm font-black text-white shadow-[0_14px_34px_rgba(88,74,150,0.12)]`}
          >
            {mood.label}
          </button>
        ))}
      </div>
    </section>
  );
}

function PlatformsPanel() {
  return (
    <section className="rounded-3xl bg-white/64 p-4 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-base font-black text-slate-950">Platforms</h2>
        <span className="text-xs font-black text-slate-400">Sources</span>
      </div>
      <div className="space-y-2">
        {platformCatalog.map((platform) => (
          <Link
            key={platform.id}
            to="/music/platforms/$platformId"
            params={{ platformId: platform.id }}
            disabled={!platform.implemented}
            className="group flex items-center gap-3 rounded-2xl bg-white/62 p-3 text-left transition hover:bg-white aria-disabled:pointer-events-none aria-disabled:opacity-55"
          >
            <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-linear-to-br ${platform.gradient} text-sm font-black text-white shadow-[0_12px_26px_rgba(88,74,150,0.14)]`}>
              {platform.icon}
            </div>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-black text-slate-950">{platform.name}</p>
              <p className="text-xs font-semibold text-slate-500">{platform.implemented ? 'Available' : 'Coming soon'}</p>
            </div>
            <span className="text-sm font-black text-violet-500 opacity-0 transition group-hover:opacity-100">&gt;</span>
          </Link>
        ))}
      </div>
    </section>
  );
}

export function MusicHomeDashboard({ library }: { library: MusicLibraryResponse }) {
  const [query, setQuery] = useState('');
  const songs = library.songs;
  const featuredSong = songs[0] ?? null;
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

  return (
    <MusicPageShell library={library} activeSong={featuredSong ?? undefined}>
      <div className="grid gap-6 xl:grid-cols-[1fr_278px] 2xl:grid-cols-[1fr_320px]">
        <div className="min-w-0 space-y-7">
          <HeroPanel library={library} featuredSong={featuredSong} />
          <PlaylistStrip playlists={library.playlists} />
          <SongTable songs={filteredSongs} query={query} onQueryChange={setQuery} />
        </div>

        <aside className="space-y-5">
          <CompactSongPanel title="Added Recently" songs={songs.slice(0, 5)} />
          <CompactSongPanel title="Old Bangers" songs={[...songs].reverse().slice(0, 5)} ranked />
          <MoodPanel />
          <PlatformsPanel />
        </aside>
      </div>
    </MusicPageShell>
  );
}
