import { Link } from '@tanstack/react-router';
import { useEffect, useState, type ReactNode } from 'react';
import {
  MusicPlatformIcon,
  MusicUiIcon,
  musicLibraryApi,
  type MusicLibraryResponse,
  type MusicLibrarySong,
  type PlatformId,
} from '@cantaro/client-shared/music';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { formatDuration, isPlatformId, platformName, songArtist, songArtwork } from './musicPresentation';


interface SongDerivedMetadata {
  platformIds: PlatformId[];
  hasMusicBrainzSource: boolean;
  matchLabel: string;
  matchTone: 'ready' | 'warning' | 'neutral';
}

function getMatchTone(matchStatus?: string): SongDerivedMetadata['matchTone'] {
  const normalizedStatus = matchStatus?.toLowerCase() ?? '';

  if (/ambiguous|candidate|no[_ -]?match|review|unmatched/.test(normalizedStatus)) return 'warning';
  if (/match/.test(normalizedStatus)) return 'ready';
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

function SongArtworkBlock({ song, index = 0, className = '' }: { song: MusicLibrarySong; index?: number; className?: string }) {
  return (
    <div className={`relative overflow-hidden rounded-[1.35rem] bg-slate-950 shadow-[0_24px_60px_rgba(15,23,42,0.18)] sm:rounded-[1.6rem] ${className}`}>
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

function DetailMetric({ label, value, compact = false }: { label: string; value: ReactNode; compact?: boolean }) {
  return (
    <div className={`rounded-2xl bg-white/64 ${compact ? 'p-2.5' : 'p-3 sm:p-4'}`}>
      <p className="text-[0.66rem] font-black tracking-[0.14em] text-slate-500 uppercase">{label}</p>
      <div className={`${compact ? 'mt-0.5' : 'mt-1'} text-sm font-black text-slate-950`}>{value}</div>
    </div>
  );
}

function DetailActionButton({
  children,
  icon,
  onClick,
  compactOnNarrow = false,
  primary = false,
}: {
  children: ReactNode;
  icon: ReactNode;
  onClick?: () => void;
  compactOnNarrow?: boolean;
  primary?: boolean;
}) {
  const label = typeof children === 'string' ? children : undefined;

  return (
    <button
      type="button"
      className={`inline-flex h-10 items-center justify-center gap-2 rounded-2xl text-sm font-black transition sm:h-11 ${
        compactOnNarrow ? 'w-10 px-0 sm:w-11 sm:px-0 xl:w-auto xl:px-4' : 'px-3 sm:px-4'
      } ${
        primary
          ? 'bg-slate-950 text-white shadow-[0_16px_34px_rgba(15,23,42,0.18)] hover:bg-slate-800'
          : 'border border-[#e3def8] bg-white/74 text-slate-700 hover:bg-white'
      }`}
      aria-label={label}
      onClick={onClick}
    >
      {icon}
      <span className={compactOnNarrow ? 'sr-only xl:not-sr-only' : undefined}>{children}</span>
    </button>
  );
}

function SongMetrics({ song, metadata, className = '' }: { song: MusicLibrarySong; metadata: SongDerivedMetadata; className?: string }) {
  return (
    <div className={`grid grid-cols-3 gap-2 sm:gap-3 ${className}`}>
      <DetailMetric compact label="Duration" value={formatDuration(song.durationSeconds)} />
      <DetailMetric compact label="Playlists" value={song.playlists.length.toLocaleString()} />
      <DetailMetric compact label="Sources" value={(metadata.platformIds.length + (metadata.hasMusicBrainzSource ? 1 : 0)).toLocaleString()} />
    </div>
  );
}

function SongHeroActions({ onPlayPreview, className = '' }: { onPlayPreview: () => void; className?: string }) {
  return (
    <div className={`flex flex-wrap gap-2 ${className}`}>
      <DetailActionButton primary icon={<MusicUiIcon name="play" className="h-4 w-4" />} onClick={onPlayPreview}>
        Play preview
      </DetailActionButton>
      <DetailActionButton compactOnNarrow icon={<MusicUiIcon name="sparkles" className="h-4 w-4" />}>
        Match
      </DetailActionButton>
      <DetailActionButton compactOnNarrow icon={<MusicUiIcon name="heart" className="h-4 w-4" />}>
        Favorite
      </DetailActionButton>
      <DetailActionButton compactOnNarrow icon={<MusicUiIcon name="more" className="h-4 w-4" />}>
        More
      </DetailActionButton>
    </div>
  );
}

function SongDetailHero({
  song,
  metadata,
  songIndex,
  onPlayPreview,
}: {
  song: MusicLibrarySong;
  metadata: SongDerivedMetadata;
  songIndex: number;
  onPlayPreview: () => void;
}) {
  return (
    <section className="music-detail-hero relative overflow-visible rounded-[1.5rem] bg-[#ece9ff] px-4 pt-4 pb-3 shadow-[0_28px_90px_rgba(88,74,150,0.12)] sm:rounded-[2rem] sm:px-5 sm:pt-5 sm:pb-4 md:px-6 md:pt-6 md:pb-5 xl:px-7 xl:pt-7">
      <div className="music-detail-hero__wash absolute inset-0 overflow-hidden rounded-[inherit] bg-[radial-gradient(circle_at_16%_12%,rgba(255,255,255,0.96),transparent_30%),radial-gradient(circle_at_82%_18%,rgba(199,210,254,0.84),transparent_34%),linear-gradient(135deg,rgba(255,255,255,0.72),rgba(224,231,255,0.72))]" />
      <div className="absolute top-0 right-5 z-20 -translate-y-1/2 sm:right-7">
        <SongStatusBadge metadata={metadata} />
      </div>
      <div className="relative">
        <div className="grid grid-cols-[96px_minmax(0,1fr)] items-start gap-x-4 gap-y-4 sm:grid-cols-[136px_minmax(0,1fr)] sm:gap-x-5 md:grid-cols-[168px_minmax(0,1fr)] xl:grid-cols-[184px_minmax(0,1fr)]">
          <SongArtworkBlock song={song} index={songIndex} className="w-full" />
          <div className="min-w-0">
            <h1 className="text-2xl leading-tight font-black text-slate-950 sm:text-4xl xl:text-5xl">{song.title}</h1>
            <p className="mt-1.5 text-base font-black text-violet-700 sm:mt-2 sm:text-xl">{songArtist(song)}</p>
          </div>
          <div className="col-span-2 flex min-w-0 flex-wrap items-center justify-end gap-3 md:gap-4">
            <SongMetrics song={song} metadata={metadata} className="w-full max-w-[24rem] sm:w-auto sm:flex-1" />
            <SongHeroActions onPlayPreview={onPlayPreview} className="justify-end" />
          </div>
        </div>
      </div>
    </section>
  );
}

function PlatformAvailabilityCard({ song }: { song: MusicLibrarySong }) {
  const metadata = getSongMetadata(song);
  const visiblePlatforms = metadata.platformIds.length > 0 ? metadata.platformIds : [];

  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Platform availability" detail="Where Cantaro has seen this song so far." />
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        {song.platformLinks.map((link) => {
          const platformId = link.platform === 'youtube-music' ? 'youtube' : link.platform;
          return (
          <a key={link.url} href={link.url} target="_blank" rel="noreferrer" className="flex items-center justify-between gap-3 rounded-2xl bg-white/66 p-3 transition hover:bg-white focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none">
            <span className="flex min-w-0 items-center gap-2 text-sm font-black text-slate-800">
              {isPlatformId(platformId) ? <MusicPlatformIcon platformId={platformId} className="h-5 w-5" /> : <MusicUiIcon name="externalLink" className="h-5 w-5" />}
              {link.label}
            </span>
            <MusicUiIcon name="externalLink" className="h-4 w-4 text-slate-400" />
          </a>
          );
        })}
        {song.platformLinks.length === 0 && visiblePlatforms.map((platformId) => (
          <div key={platformId} className="flex items-center gap-2 rounded-2xl bg-white/66 p-3 text-sm font-black text-slate-700">
            <MusicPlatformIcon platformId={platformId} className="h-5 w-5" />
            {platformName(platformId)} source known; no outbound link available
          </div>
        ))}
        {song.platformLinks.length === 0 && visiblePlatforms.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-[#d8d0f4] bg-white/48 p-4 text-sm font-semibold text-slate-500">
            No connected platform source is attached to this song yet.
          </div>
        ) : null}
      </div>
    </section>
  );
}

function PlaylistAppearancesCard({ song, library }: { song: MusicLibrarySong; library: MusicLibraryResponse }) {
  const [memberships, setMemberships] = useState(song.playlists);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [confirming, setConfirming] = useState<string>();
  const [busy, setBusy] = useState<string>();
  const [message, setMessage] = useState<string>();
  const youtubeIds = song.sourceIdentities.filter((identity) => identity.source === 'youtube').map((identity) => identity.externalId);
  const [selectedYouTubeId, setSelectedYouTubeId] = useState(youtubeIds.length === 1 ? youtubeIds[0] : '');
  useEffect(() => { setMemberships(song.playlists); setConfirming(undefined); setPickerOpen(false); }, [song]);
  const available = library.playlists.filter((playlist) => !memberships.some((item) => item.playlistId === playlist.id));
  const add = async (playlistId: string) => {
    const playlist = library.playlists.find((item) => item.id === playlistId);
    if (!playlist) return;
    setBusy(playlistId); setMessage(undefined);
    try {
      if (youtubeIds.length > 1 && !selectedYouTubeId) throw new Error('Choose which YouTube version to sync.');
      await musicLibraryApi.addSongToPlaylist(song.id, playlistId, selectedYouTubeId || undefined);
      setMemberships((current) => [...current, { playlistId, playlistName: playlist.name, position: playlist.entryCount }]);
      setMessage(`Added to ${playlist.name} and synced.`); setPickerOpen(false);
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not add this song.'); }
    finally { setBusy(undefined); }
  };
  const remove = async (playlistId: string) => {
    const playlist = memberships.find((item) => item.playlistId === playlistId);
    setBusy(playlistId); setMessage(undefined);
    try {
      await musicLibraryApi.removeSongFromPlaylist(song.id, playlistId, selectedYouTubeId || undefined);
      setMemberships((current) => current.filter((item) => item.playlistId !== playlistId));
      setMessage(`Removed from ${playlist?.playlistName ?? 'playlist'} and synced.`); setConfirming(undefined);
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not remove this song.'); }
    finally { setBusy(undefined); }
  };
  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Playlist appearances" detail="The tracked playlists that contain this song." action={available.length > 0 ? <button type="button" onClick={() => setPickerOpen((value) => !value)} className="inline-flex h-9 w-9 items-center justify-center rounded-xl bg-violet-100 text-xl font-black text-violet-800 transition hover:bg-violet-200 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none" aria-expanded={pickerOpen} aria-label="Add to another playlist">+</button> : undefined} />
      {youtubeIds.length > 1 ? <label className="mt-4 block text-xs font-black text-slate-600">YouTube version<select value={selectedYouTubeId} onChange={(event) => setSelectedYouTubeId(event.target.value)} className="mt-1 block w-full rounded-xl border border-violet-200 bg-white px-3 py-2 text-sm"><option value="">Choose a version to sync…</option>{youtubeIds.map((id) => <option key={id} value={id}>{id}</option>)}</select></label> : null}
      {pickerOpen ? <div className="mt-4 flex flex-wrap gap-2 rounded-xl bg-violet-50 p-3">{available.map((playlist) => <button key={playlist.id} type="button" disabled={Boolean(busy)} onClick={() => void add(playlist.id)} className="rounded-lg bg-white px-3 py-2 text-xs font-black text-violet-800 transition hover:bg-violet-100 disabled:opacity-50">{busy === playlist.id ? 'Adding…' : playlist.name}</button>)}</div> : null}
      <div className="mt-4 space-y-2">
        {memberships.length > 0 ? memberships.map((playlist) => (
          <div key={`${playlist.playlistId}-${playlist.position}`} className="group flex items-center gap-2 rounded-2xl bg-white/64 p-2 transition hover:bg-white">
          <Link
            to="/music/playlists/$playlistId"
            params={{ playlistId: playlist.playlistId }}
            className="flex min-w-0 flex-1 items-center justify-between gap-3 rounded-xl p-1 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
          >
            <span className="min-w-0">
              <span className="block truncate text-sm font-black text-slate-950">{playlist.playlistName}</span>
              <span className="block text-xs font-semibold text-slate-500">Position {playlist.position.toLocaleString()}</span>
            </span>
            <MusicUiIcon name="arrowRight" className="h-4 w-4 text-slate-400" />
          </Link>
          {confirming === playlist.playlistId ? <div className="flex items-center gap-1"><span className="text-xs font-bold text-rose-700">Remove?</span><button type="button" disabled={Boolean(busy)} onClick={() => void remove(playlist.playlistId)} className="rounded-lg bg-rose-700 px-2 py-1 text-xs font-black text-white disabled:opacity-50">{busy === playlist.playlistId ? 'Removing…' : 'Confirm'}</button><button type="button" onClick={() => setConfirming(undefined)} className="rounded-lg px-2 py-1 text-xs font-black text-slate-600">Cancel</button></div> : <button type="button" onClick={() => setConfirming(playlist.playlistId)} className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-rose-700 opacity-0 transition group-hover:opacity-100 hover:bg-rose-100 focus-visible:opacity-100 focus-visible:ring-2 focus-visible:ring-rose-500 focus-visible:outline-none" aria-label={`Remove from ${playlist.playlistName}`}><MusicUiIcon name="trash" className="h-4 w-4" /></button>}
          </div>
        )) : (
          <p className="rounded-2xl border border-dashed border-[#d8d0f4] bg-white/48 p-4 text-sm font-semibold text-slate-500">
            This song is not attached to a synced playlist yet.
          </p>
        )}
      </div>
      {message ? <p className="mt-3 text-sm font-semibold text-slate-600" role="status">{message}</p> : null}
    </section>
  );
}

function MetadataReadinessCard({ song }: { song: MusicLibrarySong }) {
  const rows = [
    ...(song.albums.length > 0 ? [{ label: 'Albums', value: song.albums.join(', ') }] : []),
    ...song.sourceIdentities.map((identity) => ({ label: identity.source === 'musicbrainz' ? 'MusicBrainz recording' : identity.source.toUpperCase(), value: identity.externalId })),
    { label: 'Lyrics', value: 'Ready for a future lyrics provider' },
    { label: 'Credits', value: 'Waiting on richer metadata' },
    { label: 'Platform links', value: song.platformLinks.length > 0 ? `${song.platformLinks.length} available` : 'No platform link yet' },
  ];

  return (
    <section className={panelClassName('p-5')}>
      <SectionHeader title="Archive metadata" detail="What Cantaro knows now, and what is still waiting on richer sources." />
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

function MissingSongPage({ library, detail = 'Cantaro could not find this song in the current library snapshot.' }: { library: MusicLibraryResponse; detail?: string }) {
  return (
    <MusicPageShell library={library}>
      <div className="mx-auto max-w-2xl">
        <MusicEmptyPanel
          title="Song not found"
          detail={detail}
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
  const [canonicalSong, setCanonicalSong] = useState<MusicLibrarySong | undefined>();
  const [canonicalState, setCanonicalState] = useState<'idle' | 'loading' | 'error'>('idle');
  const librarySong = library.songs.find((candidate) => candidate.id === songId);
  useEffect(() => {
    setCanonicalSong(undefined);
    if (librarySong) { setCanonicalState('idle'); return; }
    let active = true;
    setCanonicalState('loading');
    void musicLibraryApi.getCanonicalSong(songId).then((song) => { if (active) { setCanonicalSong(song); setCanonicalState('idle'); } }).catch(() => { if (active) setCanonicalState('error'); });
    return () => { active = false; };
  }, [librarySong, songId]);
  const song = librarySong ?? canonicalSong;
  const activeSong = library.songs.find((candidate) => candidate.id === activeSongId);

  if (!song && canonicalState === 'loading') return <MusicPageShell library={library}><div className="mx-auto max-w-2xl rounded-2xl bg-white/70 p-6 text-sm font-bold text-slate-600" role="status">Loading song details…</div></MusicPageShell>;
  if (!song) {
    return <MissingSongPage library={library} detail={canonicalState === 'error' ? 'Cantaro could not load this canonical song. Check the connection and try again.' : undefined} />;
  }

  const metadata = getSongMetadata(song);
  const songIndex = Math.max(library.songs.findIndex((candidate) => candidate.id === song.id), 0);

  return (
    <MusicPageShell library={library} activeSong={activeSong} onStopActiveSong={() => setActiveSongId(undefined)}>
      <div>
        <Link
          to="/music/songs"
          className="inline-flex items-center gap-2 text-sm font-black text-violet-600 transition hover:text-violet-500"
        >
          <MusicUiIcon name="arrowLeft" className="h-4 w-4" />
          Back to song library
        </Link>

        <div className="mt-5 space-y-3">
          <SongDetailHero
            song={song}
            metadata={metadata}
            songIndex={songIndex}
            onPlayPreview={() => setActiveSongId(song.id)}
          />

          <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
            <div className="min-w-0 space-y-5">
              <PlatformAvailabilityCard song={song} />
              <VersionsCard song={song} />
              <PlaylistAppearancesCard song={song} library={library} />
            </div>
            <aside className="space-y-5">
              <MetadataReadinessCard song={song} />
              <RelatedSongsCard library={library} song={song} />
            </aside>
          </div>
        </div>
      </div>
    </MusicPageShell>
  );
}
