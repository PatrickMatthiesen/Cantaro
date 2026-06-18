import { Link } from '@tanstack/react-router';
import { useState, type ReactNode } from 'react';
import {
  MusicPlatformIcon,
  MusicUiIcon,
  platformCatalog,
  type MusicLibraryResponse,
  type MusicLibrarySong,
  type PlatformId,
} from '@cantaro/client-shared/music';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { formatDuration, platformName, songArtist, songArtwork } from './musicPresentation';


interface SongDerivedMetadata {
  platformIds: PlatformId[];
  hasMusicBrainzSource: boolean;
  matchLabel: string;
  matchTone: 'ready' | 'warning' | 'neutral';
}

function isPlatformId(value: string): value is PlatformId {
  return platformCatalog.some((platform) => platform.id === value);
}

function getMatchTone(matchStatus?: string): SongDerivedMetadata['matchTone'] {
  const normalizedStatus = matchStatus?.toLowerCase() ?? '';

  if (/match/.test(normalizedStatus) && !/unmatched/.test(normalizedStatus)) return 'ready';
  if (/review|candidate|unmatched/.test(normalizedStatus)) return 'warning';
  return 'neutral';
}

function getSongMetadata(song: MusicLibrarySong): SongDerivedMetadata {
  const normalizedStatus = song.matchStatus?.toLowerCase() ?? '';

  return {
    platformIds: song.sourcePlatforms.filter(isPlatformId),
    hasMusicBrainzSource: song.sourcePlatforms.includes('musicbrainz'),
    matchLabel: song.matchStatus ? song.matchStatus.replace(/[-_]/g, ' ') : 'Library track',
    matchTone: getMatchTone(normalizedStatus),
  };
}

function matchToneClassName(tone: SongDerivedMetadata['matchTone']) {
  if (tone === 'ready') return 'border-emerald-200 bg-emerald-50 text-emerald-700';
  if (tone === 'warning') return 'border-amber-200 bg-amber-50 text-amber-700';
  return 'border-slate-200 bg-slate-50 text-slate-600';
}

function panelClassName(extra = '') {
  return `rounded-[1.35rem] border border-white/70 bg-white/70 shadow-[0_20px_70px_rgba(88,74,150,0.08)] backdrop-blur-xl ${extra}`;
}

function SectionHeader({ title, detail, action }: { title: string; detail?: string; action?: ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="min-w-0">
        <h2 className="text-base font-black text-slate-950">{title}</h2>
        {detail ? <p className="mt-1 text-sm font-semibold text-slate-500">{detail}</p> : null}
      </div>
      {action}
    </div>
  );
}

function PlatformPill({ platformId }: { platformId: PlatformId }) {
  return (
    <span className="inline-flex h-9 items-center gap-2 rounded-full border border-[#e7e0fb] bg-white/76 px-3 text-xs font-black text-slate-800">
      <MusicPlatformIcon platformId={platformId} className="h-4 w-4" title={platformName(platformId)} />
      {platformName(platformId)}
    </span>
  );
}

function EmptyPlatformPill() {
  return (
    <span className="inline-flex h-9 items-center rounded-full border border-slate-200 bg-white/70 px-3 text-xs font-black text-slate-500">
      Local library
    </span>
  );
}

function SongArtworkBlock({ song, index = 0 }: { song: MusicLibrarySong; index?: number }) {
  return (
    <div className="relative overflow-hidden rounded-[1.6rem] bg-slate-950 shadow-[0_24px_60px_rgba(15,23,42,0.18)]">
      <img src={songArtwork(song, index)} alt="" className="aspect-square w-full object-cover" />
      <div className="pointer-events-none absolute inset-0 bg-linear-to-t from-slate-950/30 via-transparent to-white/5" />
    </div>
  );
}

function SongStatusBadge({ metadata }: { metadata: SongDerivedMetadata }) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-black capitalize ${matchToneClassName(metadata.matchTone)}`}>
      <MusicUiIcon name={metadata.matchTone === 'ready' ? 'checkCircle' : metadata.matchTone === 'warning' ? 'warning' : 'database'} className="h-3.5 w-3.5" />
      {metadata.matchLabel}
    </span>
  );
}

function DetailMetric({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="rounded-2xl bg-white/64 p-4">
      <p className="text-[0.66rem] font-black tracking-[0.14em] text-slate-500 uppercase">{label}</p>
      <div className="mt-1 text-sm font-black text-slate-950">{value}</div>
    </div>
  );
}

function DetailActionButton({
  children,
  icon,
  onClick,
  primary = false,
}: {
  children: ReactNode;
  icon: ReactNode;
  onClick?: () => void;
  primary?: boolean;
}) {
  return (
    <button
      type="button"
      className={`inline-flex h-11 items-center justify-center gap-2 rounded-2xl px-4 text-sm font-black transition ${
        primary
          ? 'bg-slate-950 text-white shadow-[0_16px_34px_rgba(15,23,42,0.18)] hover:bg-slate-800'
          : 'border border-[#e3def8] bg-white/74 text-slate-700 hover:bg-white'
      }`}
      onClick={onClick}
    >
      {icon}
      {children}
    </button>
  );
}

function PlatformAvailabilityCard({ song }: { song: MusicLibrarySong }) {
  const metadata = getSongMetadata(song);
  const visiblePlatforms = metadata.platformIds.length > 0 ? metadata.platformIds : [];

  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Platform availability" detail="Where Cantaro has seen this song so far." />
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        {visiblePlatforms.map((platformId) => (
          <div key={platformId} className="flex items-center justify-between gap-3 rounded-2xl bg-white/66 p-3">
            <span className="flex min-w-0 items-center gap-2 text-sm font-black text-slate-800">
              <MusicPlatformIcon platformId={platformId} className="h-5 w-5" />
              {platformName(platformId)}
            </span>
            <span className="rounded-full bg-emerald-50 px-2.5 py-1 text-[0.68rem] font-black text-emerald-700">Known</span>
          </div>
        ))}
        {visiblePlatforms.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-[#d8d0f4] bg-white/48 p-4 text-sm font-semibold text-slate-500">
            No connected platform source is attached to this song yet.
          </div>
        ) : null}
      </div>
    </section>
  );
}

function PlaylistAppearancesCard({ song }: { song: MusicLibrarySong }) {
  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Playlist appearances" detail="The places this song currently appears inside Cantaro." />
      <div className="mt-4 space-y-2">
        {song.playlists.length > 0 ? song.playlists.map((playlist) => (
          <Link
            key={`${playlist.playlistId}-${playlist.position}`}
            to="/music/playlists/$playlistId"
            params={{ playlistId: playlist.playlistId }}
            className="flex items-center justify-between gap-3 rounded-2xl bg-white/64 p-3 transition hover:bg-white focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
          >
            <span className="min-w-0">
              <span className="block truncate text-sm font-black text-slate-950">{playlist.playlistName}</span>
              <span className="block text-xs font-semibold text-slate-500">Position {playlist.position.toLocaleString()}</span>
            </span>
            <MusicUiIcon name="arrowRight" className="h-4 w-4 text-slate-400" />
          </Link>
        )) : (
          <p className="rounded-2xl border border-dashed border-[#d8d0f4] bg-white/48 p-4 text-sm font-semibold text-slate-500">
            This song is not attached to a synced playlist yet.
          </p>
        )}
      </div>
    </section>
  );
}

function MetadataReadinessCard({ song }: { song: MusicLibrarySong }) {
  const metadata = getSongMetadata(song);
  const rows = [
    ...(song.albums.length > 0 ? [{ label: 'Albums', value: song.albums.join(', ') }] : []),
    { label: 'MusicBrainz identifier', value: metadata.hasMusicBrainzSource ? 'MusicBrainz source detected' : 'Not attached yet' },
    { label: 'Lyrics', value: 'Ready for a future lyrics provider' },
    { label: 'Credits', value: 'Waiting on richer metadata' },
    { label: 'Platform links', value: metadata.platformIds.length > 0 ? 'Source platform known' : 'No platform link yet' },
  ];

  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Metadata readiness" detail="What this detail page can grow into as the backend learns more." />
      <div className="mt-4 divide-y divide-[#eeeafa] overflow-hidden rounded-2xl bg-white/58">
        {rows.map((row) => (
          <div key={row.label} className="grid gap-2 px-4 py-3 text-sm sm:grid-cols-[180px_1fr]">
            <span className="font-black text-slate-600">{row.label}</span>
            <span className="font-semibold text-slate-500">{row.value}</span>
          </div>
        ))}
      </div>
    </section>
  );
}

function VersionsCard({ song }: { song: MusicLibrarySong }) {
  const metadata = getSongMetadata(song);
  const versionRows = metadata.platformIds.length > 0
    ? metadata.platformIds.map((platformId) => ({
        id: platformId,
        source: platformName(platformId),
        version: 'Library version',
        status: 'Observed',
      }))
    : [{ id: 'library', source: 'Cantaro', version: 'Library version', status: 'Observed' }];

  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Versions and variants" detail="A first version view; remix, live, cover, and acoustic variants can land here later." />
      <div className="mt-4 overflow-hidden rounded-2xl bg-white/58">
        <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_96px] gap-3 border-b border-[#eeeafa] px-4 py-3 text-[0.68rem] font-black tracking-[0.14em] text-slate-500 uppercase">
          <span>Source</span>
          <span>Version</span>
          <span>Status</span>
        </div>
        {versionRows.map((row) => (
          <div key={row.id} className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_96px] gap-3 border-b border-[#eeeafa] px-4 py-3 text-sm last:border-b-0">
            <span className="truncate font-black text-slate-800">{row.source}</span>
            <span className="truncate font-semibold text-slate-600">{row.version}</span>
            <span className="text-xs font-black text-emerald-700">{row.status}</span>
          </div>
        ))}
      </div>
    </section>
  );
}

function RelatedSongsCard({ library, song }: { library: MusicLibraryResponse; song: MusicLibrarySong }) {
  const artist = song.artist?.toLowerCase();
  const relatedSongs = library.songs
    .filter((candidate) => candidate.id !== song.id && (artist ? candidate.artist?.toLowerCase() === artist : true))
    .slice(0, 4);

  if (relatedSongs.length === 0) return null;

  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Related songs" detail="Nearby tracks from the current library." />
      <div className="mt-4 space-y-2">
        {relatedSongs.map((relatedSong, index) => (
          <Link
            key={relatedSong.id}
            to="/music/songs/$songId"
            params={{ songId: relatedSong.id }}
            className="flex items-center gap-3 rounded-2xl p-2 transition hover:bg-white/64 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
          >
            <img src={songArtwork(relatedSong, index)} alt="" className="h-12 w-12 rounded-xl object-cover" />
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-black text-slate-950">{relatedSong.title}</span>
              <span className="block truncate text-xs font-semibold text-slate-500">{songArtist(relatedSong)}</span>
            </span>
            <span className="font-mono text-xs text-slate-500">{formatDuration(relatedSong.durationSeconds)}</span>
          </Link>
        ))}
      </div>
    </section>
  );
}

function MissingSongPage({ library }: { library: MusicLibraryResponse }) {
  return (
    <MusicPageShell library={library}>
      <div className="mx-auto max-w-2xl">
        <MusicEmptyPanel
          title="Song not found"
          detail="Cantaro could not find this song in the current library snapshot."
        />
        <Link
          to="/music/songs"
          className="mt-4 inline-flex items-center gap-2 text-sm font-black text-violet-600 transition hover:text-violet-500"
        >
          <MusicUiIcon name="arrowLeft" className="h-4 w-4" />
          Back to songs
        </Link>
      </div>
    </MusicPageShell>
  );
}

// fallow-ignore-next-line complexity
export function MusicSongDetailPage({ library, songId }: { library: MusicLibraryResponse; songId: string }) {
  const [activeSongId, setActiveSongId] = useState<string | undefined>();
  const song = library.songs.find((candidate) => candidate.id === songId);
  const activeSong = library.songs.find((candidate) => candidate.id === activeSongId);

  if (!song) {
    return <MissingSongPage library={library} />;
  }

  const metadata = getSongMetadata(song);
  const songIndex = Math.max(library.songs.findIndex((candidate) => candidate.id === song.id), 0);

  return (
    <MusicPageShell library={library} activeSong={activeSong} onStopActiveSong={() => setActiveSongId(undefined)}>
      <div className="space-y-5">
        <Link
          to="/music/songs"
          className="inline-flex items-center gap-2 text-sm font-black text-violet-600 transition hover:text-violet-500"
        >
          <MusicUiIcon name="arrowLeft" className="h-4 w-4" />
          Back to song library
        </Link>

        <section className="relative overflow-hidden rounded-[2rem] bg-[#ece9ff] p-5 shadow-[0_28px_90px_rgba(88,74,150,0.12)] md:p-7">
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_16%_12%,rgba(255,255,255,0.96),transparent_30%),radial-gradient(circle_at_82%_18%,rgba(199,210,254,0.84),transparent_34%),linear-gradient(135deg,rgba(255,255,255,0.72),rgba(224,231,255,0.72))]" />
          <div className="relative grid gap-6 xl:grid-cols-[minmax(220px,320px)_1fr] xl:items-end">
            <SongArtworkBlock song={song} index={songIndex} />
            <div className="min-w-0 space-y-5">
              <div>
                <SongStatusBadge metadata={metadata} />
                <h1 className="mt-3 text-4xl leading-tight font-black text-slate-950 sm:text-5xl">{song.title}</h1>
                <p className="mt-2 text-xl font-black text-violet-700">{songArtist(song)}</p>
              </div>
              <div className="flex flex-wrap gap-2">
                {metadata.platformIds.length > 0 ? metadata.platformIds.map((platformId) => <PlatformPill key={platformId} platformId={platformId} />) : <EmptyPlatformPill />}
              </div>
              <div className="grid gap-3 sm:grid-cols-3">
                <DetailMetric label="Duration" value={formatDuration(song.durationSeconds)} />
                <DetailMetric label="Playlists" value={song.playlists.length.toLocaleString()} />
                <DetailMetric label="Sources" value={(metadata.platformIds.length + (metadata.hasMusicBrainzSource ? 1 : 0)).toLocaleString()} />
              </div>
              <div className="flex flex-wrap gap-2">
                <DetailActionButton primary icon={<MusicUiIcon name="play" className="h-4 w-4" />} onClick={() => setActiveSongId(song.id)}>
                  Play preview
                </DetailActionButton>
                <DetailActionButton icon={<MusicUiIcon name="sparkles" className="h-4 w-4" />}>
                  Match across platforms
                </DetailActionButton>
                <DetailActionButton icon={<MusicUiIcon name="heart" className="h-4 w-4" />}>
                  Favorite
                </DetailActionButton>
                <DetailActionButton icon={<MusicUiIcon name="more" className="h-4 w-4" />}>
                  More
                </DetailActionButton>
              </div>
            </div>
          </div>
        </section>

        <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
          <div className="min-w-0 space-y-5">
            <PlatformAvailabilityCard song={song} />
            <VersionsCard song={song} />
            <PlaylistAppearancesCard song={song} />
          </div>
          <aside className="space-y-5">
            <MetadataReadinessCard song={song} />
            <RelatedSongsCard library={library} song={song} />
          </aside>
        </div>
      </div>
    </MusicPageShell>
  );
}
