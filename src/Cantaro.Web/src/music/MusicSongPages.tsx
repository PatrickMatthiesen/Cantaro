import { Link } from '@tanstack/react-router';
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import {
  MusicPlatformIcon,
  MusicUiIcon,
  musicLibraryApi,
  type LyricsResult,
  type MusicLibraryResponse,
  type MusicLibrarySong,
  type PlatformId,
} from '@cantaro/client-shared/music';
import { ActionButton, SelectField, actionClassName } from '@cantaro/client-shared/ui';
import { MusicPageShell } from './MusicPageShell';
import { lyricsForDisplay } from './musicLyrics';
import { formatDuration, isPlatformId, platformName, songArtist } from './musicPresentation';

interface SongDerivedMetadata {
  platformIds: PlatformId[];
  matchLabel: string;
  matchTone: 'ready' | 'warning' | 'neutral';
  isObservation: boolean;
}

type SongPlaylistMembership = MusicLibrarySong['playlists'][number];

function mutationErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function selectedSourceId(value: string): string | undefined {
  return value || undefined;
}

function ensureYouTubeVersionSelected(youtubeIds: string[], selectedYouTubeId: string) {
  if (youtubeIds.length > 1 && !selectedYouTubeId) throw new Error('Choose which YouTube version to sync.');
}

function getMatchTone(matchStatus?: string): SongDerivedMetadata['matchTone'] {
  const normalizedStatus = matchStatus?.toLowerCase() ?? '';
  if (/ambiguous|candidate|no[_ -]?match|review|unmatched/.test(normalizedStatus)) return 'warning';
  if (/match/.test(normalizedStatus)) return 'ready';
  return 'neutral';
}

function getSongMetadata(song: MusicLibrarySong): SongDerivedMetadata {
  const matchTone = getMatchTone(song.matchStatus);
  return {
    platformIds: song.sourcePlatforms.filter(isPlatformId),
    matchLabel: matchTone === 'warning'
      ? 'Needs matching'
      : song.matchStatus?.replace(/[-_]/g, ' ') ?? 'Library track',
    matchTone,
    isObservation: song.id.startsWith('observation:'),
  };
}

function matchToneClassName(tone: SongDerivedMetadata['matchTone']) {
  if (tone === 'ready') return 'text-success-content';
  if (tone === 'warning') return 'text-warning-content';
  return 'text-immersive-content-muted';
}

function SectionHeader({ title, detail, action }: { title: string; detail?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div className="min-w-0">
        <h2 className="text-xl font-bold text-content">{title}</h2>
        {detail ? <p className="mt-1 max-w-[70ch] text-sm leading-6 text-content-muted">{detail}</p> : null}
      </div>
      {action}
    </div>
  );
}

function SongArtwork({ song, className = '' }: { song: MusicLibrarySong; className?: string }) {
  if (!song.thumbnailUrl) {
    return (
      <div className={`flex aspect-square items-center justify-center bg-surface-subtle text-content-subtle ${className}`}>
        <MusicUiIcon name="music" className="h-12 w-12" />
      </div>
    );
  }

  return <img src={song.thumbnailUrl} alt="" className={`aspect-square object-cover ${className}`} />;
}

function SongIdentityStatus({ metadata }: { metadata: SongDerivedMetadata }) {
  const icon = metadata.matchTone === 'ready' ? 'checkCircle' : metadata.matchTone === 'warning' ? 'warning' : 'database';
  return (
    <p className={`inline-flex items-center gap-2 text-sm font-semibold capitalize ${matchToneClassName(metadata.matchTone)}`}>
      <MusicUiIcon name={icon} className="h-4 w-4" />
      {metadata.matchLabel}
    </p>
  );
}

function SongMetrics({ song }: { song: MusicLibrarySong }) {
  const sourceCount = song.sourceIdentities.length || song.sourcePlatforms.length;
  const metrics = [
    ['Duration', formatDuration(song.durationSeconds)],
    ['Playlists', song.playlists.length.toLocaleString()],
    ['Sources', sourceCount.toLocaleString()],
  ];

  return (
    <dl className="mt-6 flex flex-wrap gap-x-8 gap-y-3 text-sm text-immersive-content-muted">
      {metrics.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd className="mt-1 text-base font-bold text-immersive-content">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

function SongHeroActions({ song, metadata }: { song: MusicLibrarySong; metadata: SongDerivedMetadata }) {
  const primaryLink = song.platformLinks[0];
  if (!primaryLink && metadata.matchTone !== 'warning') return null;

  return (
    <div className="mt-7 flex flex-wrap gap-3">
      {primaryLink ? (
        <a href={primaryLink.url} target="_blank" rel="noreferrer" className={actionClassName({ tone: 'personal' })}>
          <MusicUiIcon name="externalLink" className="h-4 w-4" />
          {primaryLink.label}
        </a>
      ) : null}
      {metadata.matchTone === 'warning' ? (
        <Link to="/music/matching" className={actionClassName({ tone: 'secondary' })}>
          <MusicUiIcon name="sparkles" className="h-4 w-4" />
          Review match
        </Link>
      ) : null}
    </div>
  );
}

function SongDetailHero({ song, metadata }: { song: MusicLibrarySong; metadata: SongDerivedMetadata }) {
  return (
    <section className="relative isolate overflow-hidden bg-immersive-canvas text-immersive-content">
      {song.thumbnailUrl ? (
        <img src={song.thumbnailUrl} alt="" className="absolute inset-0 h-full w-full scale-110 object-cover opacity-45 blur-2xl dark:opacity-55" aria-hidden />
      ) : null}
      <div className="absolute inset-0 bg-linear-to-r from-immersive-scrim-strong via-immersive-scrim-medium to-immersive-scrim-soft" aria-hidden />
      <div className="absolute inset-0 bg-linear-to-t from-immersive-scrim-base via-transparent to-immersive-scrim-soft" aria-hidden />

      <div className="relative z-10 flex min-h-[27rem] items-end gap-7 px-4 py-8 sm:px-7 md:px-9">
        <SongArtwork song={song} className="hidden w-48 shrink-0 bg-surface md:block xl:w-56" />
        <div className="min-w-0 max-w-4xl">
          <div className="flex items-center gap-4 md:hidden">
            <SongArtwork song={song} className="w-24 shrink-0 bg-surface sm:w-32" />
            <SongIdentityStatus metadata={metadata} />
          </div>
          <div className="hidden md:block"><SongIdentityStatus metadata={metadata} /></div>
          <h1 className="mt-4 max-w-[22ch] text-3xl leading-[1.06] font-black tracking-[-0.03em] text-balance sm:text-4xl xl:text-5xl">
            {song.title}
          </h1>
          <p className="mt-3 text-lg font-semibold text-immersive-content-muted sm:text-xl">{songArtist(song)}</p>
          <SongMetrics song={song} />
          <SongHeroActions song={song} metadata={metadata} />
        </div>
      </div>
    </section>
  );
}

function PlatformAvailabilitySection({ song }: { song: MusicLibrarySong }) {
  const metadata = getSongMetadata(song);
  const visiblePlatforms = metadata.platformIds;

  return (
    <section className="border-b border-border-subtle py-9">
      <SectionHeader title="Listen on" detail="Direct destinations Cantaro has recorded for this song." />
      {song.platformLinks.length > 0 ? (
        <ul className="mt-5 grid gap-x-10 sm:grid-cols-2">
          {song.platformLinks.map((link) => {
            const platformId = link.platform === 'youtube-music' ? 'youtube' : link.platform;
            return (
              <li key={link.url} className="border-t border-border-subtle first:border-t-0 sm:[&:nth-child(2)]:border-t-0">
                <a href={link.url} target="_blank" rel="noreferrer" className="group flex min-h-18 items-center gap-3 py-4 focus-visible:outline-2 focus-visible:outline-focus">
                  <span className="inline-flex size-11 shrink-0 items-center justify-center text-content">
                    {isPlatformId(platformId) ? <MusicPlatformIcon platformId={platformId} className="h-8 w-8" /> : <MusicUiIcon name="externalLink" className="h-6 w-6" />}
                  </span>
                  <span className="min-w-0 flex-1">
                    <strong className="block truncate text-content">{link.label}</strong>
                    <span className="mt-0.5 block text-sm text-content-muted">Open external destination</span>
                  </span>
                  <MusicUiIcon name="externalLink" className="h-4 w-4 text-content-subtle transition-colors group-hover:text-personal-accent-strong" />
                </a>
              </li>
            );
          })}
        </ul>
      ) : visiblePlatforms.length > 0 ? (
        <ul className="mt-5 divide-y divide-border-subtle">
          {visiblePlatforms.map((platformId) => (
            <li key={platformId} className="flex items-center gap-3 py-4 text-sm text-content-muted">
              <MusicPlatformIcon platformId={platformId} className="h-7 w-7" />
              {platformName(platformId)} identity known; no external destination is available.
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-5 py-4 text-content-muted">No platform destination is attached to this song yet.</p>
      )}
    </section>
  );
}

type LyricsView =
  | { kind: 'closed' }
  | { kind: 'loading' }
  | { kind: 'ready'; result: LyricsResult }
  | { kind: 'error'; message: string };

const lyricsNotices = {
  available: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
  instrumental: { title: 'Instrumental track', fallback: 'This recording is marked as instrumental.' },
  ambiguous: { title: 'Lyrics need review', fallback: 'Cantaro found more than one possible match, so it did not choose one.' },
  provider_error: { title: 'Lyrics provider unavailable', fallback: 'The lyrics source could not be reached. Try again in a moment.' },
  disabled: { title: 'Lyrics lookup is disabled', fallback: 'Lyrics lookup is disabled for this Cantaro server.' },
  unavailable: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
} satisfies Record<LyricsResult['state'], { title: string; fallback: string }>;

function LyricsLoading() {
  return (
    <div className="mt-5 space-y-3" aria-label="Loading lyrics" role="status">
      <div className="h-3 w-11/12 bg-surface-hover motion-safe:animate-pulse" />
      <div className="h-3 w-4/5 bg-surface-hover motion-safe:animate-pulse" />
      <div className="h-3 w-9/12 bg-surface-hover motion-safe:animate-pulse" />
    </div>
  );
}

function LyricsNotice({ result }: { result: LyricsResult }) {
  const notice = lyricsNotices[result.state];
  return (
    <div className="mt-5 border-y border-border-subtle py-5">
      <p className="font-bold text-content">{notice.title}</p>
      <p className="mt-1 max-w-[70ch] text-sm leading-6 text-content-muted">{result.explanation || notice.fallback}</p>
      {result.attribution ? <p className="mt-3 text-xs text-content-subtle">{result.attribution}</p> : null}
    </div>
  );
}

function lyricsMatchLabel(matchStatus: string) {
  const knownLabels: Record<string, string> = { exact: 'Exact match', fallback: 'Best match' };
  return knownLabels[matchStatus] ?? matchStatus.replace(/[-_]/g, ' ');
}

function AvailableLyrics({ result, text, synchronized }: { result: LyricsResult; text: string; synchronized: boolean }) {
  const confidence = result.confidence === undefined ? undefined : `${Math.round(result.confidence * 100)}% confidence`;
  return (
    <div className="mt-5">
      <p className="text-sm font-semibold text-content-muted">
        {synchronized ? 'Timed lyrics' : 'Plain lyrics'} · {lyricsMatchLabel(result.matchStatus)}{confidence ? ` · ${confidence}` : ''}
      </p>
      <pre className="mt-5 max-w-[70ch] font-sans text-[0.95rem] leading-7 font-medium break-words whitespace-pre-wrap text-content">{text}</pre>
      <p className="mt-5 border-t border-border-subtle pt-3 text-xs text-content-subtle">{result.attribution}</p>
    </div>
  );
}

function LyricsResultContent({ result }: { result: LyricsResult }) {
  const selected = result.state === 'available' ? lyricsForDisplay(result) : null;
  return selected ? <AvailableLyrics result={result} {...selected} /> : <LyricsNotice result={result} />;
}

function LyricsSection({ song }: { song: MusicLibrarySong }) {
  const [view, setView] = useState<LyricsView>({ kind: 'closed' });
  const requestId = useRef(0);
  const requestController = useRef<AbortController | null>(null);

  useEffect(() => {
    requestId.current += 1;
    requestController.current?.abort();
    requestController.current = null;
    setView({ kind: 'closed' });
    return () => requestController.current?.abort();
  }, [song.id]);

  const load = () => {
    requestController.current?.abort();
    const controller = new AbortController();
    requestController.current = controller;
    const currentRequest = ++requestId.current;
    setView({ kind: 'loading' });
    void musicLibraryApi.getLyrics(song.id, controller.signal)
      .then((result) => {
        if (requestId.current === currentRequest && !controller.signal.aborted) setView({ kind: 'ready', result });
      })
      .catch(() => {
        if (requestId.current === currentRequest && !controller.signal.aborted) setView({ kind: 'error', message: 'Lyrics could not be loaded right now.' });
      });
  };

  const close = () => {
    requestId.current += 1;
    requestController.current?.abort();
    requestController.current = null;
    setView({ kind: 'closed' });
  };

  const action = view.kind === 'closed' ? (
    <ActionButton tone="secondary" onClick={load}><MusicUiIcon name="listMusic" className="h-4 w-4" />Show lyrics</ActionButton>
  ) : (
    <ActionButton tone="ghost" onClick={close}>Hide lyrics</ActionButton>
  );

  return (
    <section className="border-b border-border-subtle py-9">
      <SectionHeader title="Lyrics" detail="Loaded on demand with match confidence and source attribution." action={action} />
      {view.kind === 'closed' ? null : view.kind === 'loading' ? (
        <LyricsLoading />
      ) : view.kind === 'error' ? (
        <div className="mt-5 border-y border-danger-border py-5">
          <p className="font-bold text-danger-content">Lyrics could not be loaded</p>
          <p className="mt-1 text-sm text-danger-content">{view.message}</p>
          <ActionButton tone="secondary" className="mt-4" onClick={load}><MusicUiIcon name="refresh" className="h-4 w-4" />Try again</ActionButton>
        </div>
      ) : (
        <LyricsResultContent result={view.result} />
      )}
    </section>
  );
}

function PlaylistAppearancesSection({ song, library }: { song: MusicLibrarySong; library: MusicLibraryResponse }) {
  const [memberships, setMemberships] = useState(song.playlists);
  const [confirming, setConfirming] = useState<string>();
  const [busy, setBusy] = useState<string>();
  const [message, setMessage] = useState<string>();
  const [selectedPlaylistId, setSelectedPlaylistId] = useState('');
  const youtubeIds = song.sourceIdentities.filter((identity) => identity.source === 'youtube').map((identity) => identity.externalId);
  const [selectedYouTubeId, setSelectedYouTubeId] = useState(youtubeIds.length === 1 ? youtubeIds[0] : '');
  const available = library.playlists.filter((playlist) => !memberships.some((item) => item.playlistId === playlist.id));

  useEffect(() => {
    setMemberships(song.playlists);
    setConfirming(undefined);
    setSelectedPlaylistId('');
    setMessage(undefined);
  }, [song]);

  const add = async (playlistId: string) => {
    const playlist = library.playlists.find((item) => item.id === playlistId);
    if (!playlist) return;
    setBusy(playlistId);
    setMessage(undefined);
    try {
      ensureYouTubeVersionSelected(youtubeIds, selectedYouTubeId);
      await musicLibraryApi.addSongToPlaylist(song.id, playlistId, selectedSourceId(selectedYouTubeId));
      setMemberships((current) => [...current, { playlistId, playlistName: playlist.name, position: playlist.entryCount }]);
      setMessage(`Added to ${playlist.name} and synced.`);
      setSelectedPlaylistId('');
    } catch (error) {
      setMessage(mutationErrorMessage(error, 'Could not add this song.'));
    } finally {
      setBusy(undefined);
    }
  };

  const remove = async (playlistId: string) => {
    const playlist = memberships.find((item) => item.playlistId === playlistId);
    setBusy(playlistId);
    setMessage(undefined);
    try {
      await musicLibraryApi.removeSongFromPlaylist(song.id, playlistId, selectedSourceId(selectedYouTubeId));
      setMemberships((current) => current.filter((item) => item.playlistId !== playlistId));
      setMessage(`Removed from ${playlist?.playlistName ?? 'playlist'} and synced.`);
      setConfirming(undefined);
    } catch (error) {
      setMessage(mutationErrorMessage(error, 'Could not remove this song.'));
    } finally {
      setBusy(undefined);
    }
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (selectedPlaylistId) void add(selectedPlaylistId);
  };

  return (
    <section className="border-b border-border-subtle py-9">
      <SectionHeader title="Playlists" detail="Synced playlists that currently contain this song." />
      {available.length > 0 ? (
        <form onSubmit={submit} className="mt-5 flex flex-wrap items-end gap-3">
          <SelectField label="Add to playlist" value={selectedPlaylistId} onChange={(event) => setSelectedPlaylistId(event.target.value)} containerClassName="min-w-[15rem] flex-1 sm:max-w-sm">
            <option value="">Choose a playlist…</option>
            {available.map((playlist) => <option key={playlist.id} value={playlist.id}>{playlist.name}</option>)}
          </SelectField>
          {youtubeIds.length > 1 ? (
            <SelectField label="YouTube version" value={selectedYouTubeId} onChange={(event) => setSelectedYouTubeId(event.target.value)} containerClassName="min-w-[15rem] flex-1 sm:max-w-sm">
              <option value="">Choose a version…</option>
              {youtubeIds.map((id) => <option key={id} value={id}>{id}</option>)}
            </SelectField>
          ) : null}
          <ActionButton tone="personal" type="submit" disabled={!selectedPlaylistId || Boolean(busy)} aria-busy={Boolean(busy)} busyLabel="Adding…">Add to playlist</ActionButton>
        </form>
      ) : null}
      <PlaylistMembershipList memberships={memberships} confirming={confirming} busy={busy} onConfirm={setConfirming} onRemove={remove} />
      {message ? <p className="mt-4 text-sm font-semibold text-content-muted" role="status">{message}</p> : null}
    </section>
  );
}

function PlaylistMembershipList({ memberships, confirming, busy, onConfirm, onRemove }: { memberships: SongPlaylistMembership[]; confirming?: string; busy?: string; onConfirm: (playlistId?: string) => void; onRemove: (playlistId: string) => Promise<void> }) {
  if (memberships.length === 0) return <p className="mt-5 py-4 text-content-muted">This song is not in a synced playlist yet.</p>;
  return (
    <ul className="mt-5 divide-y divide-border-subtle border-t border-border-subtle">
      {memberships.map((playlist) => (
        <PlaylistMembershipRow key={`${playlist.playlistId}-${playlist.position}`} playlist={playlist} confirming={confirming === playlist.playlistId} busy={busy} onConfirm={onConfirm} onRemove={onRemove} />
      ))}
    </ul>
  );
}

function PlaylistMembershipRow({ playlist, confirming, busy, onConfirm, onRemove }: { playlist: SongPlaylistMembership; confirming: boolean; busy?: string; onConfirm: (playlistId?: string) => void; onRemove: (playlistId: string) => Promise<void> }) {
  return (
    <li className="flex min-w-0 flex-col items-stretch gap-2 py-4 sm:flex-row sm:items-center sm:gap-3">
      <Link to="/music/playlists/$playlistId" params={{ playlistId: playlist.playlistId }} className="flex min-w-0 flex-1 items-center justify-between gap-3 focus-visible:outline-2 focus-visible:outline-focus">
        <span className="min-w-0">
          <strong className="block truncate text-content">{playlist.playlistName}</strong>
          <span className="mt-0.5 block text-sm text-content-muted">Position {playlist.position.toLocaleString()}</span>
        </span>
        <MusicUiIcon name="arrowRight" className="h-4 w-4 text-content-subtle" />
      </Link>
      {confirming ? (
        <div className="flex flex-wrap items-center gap-2 sm:justify-end">
          <span className="text-sm font-semibold text-danger-content">Remove?</span>
          <ActionButton tone="danger" disabled={Boolean(busy)} onClick={() => void onRemove(playlist.playlistId)}>{busy ? 'Removing…' : 'Confirm'}</ActionButton>
          <ActionButton tone="ghost" onClick={() => onConfirm(undefined)}>Cancel</ActionButton>
        </div>
      ) : (
        <ActionButton tone="ghost" className="self-start hover:bg-danger-surface hover:text-danger-content sm:self-auto" onClick={() => onConfirm(playlist.playlistId)}>Remove</ActionButton>
      )}
    </li>
  );
}

function ArchiveMetadataSection({ song, metadata }: { song: MusicLibrarySong; metadata: SongDerivedMetadata }) {
  const rows = [
    ...(song.albums.length > 0 ? [{ label: 'Albums', value: song.albums.join(', ') }] : []),
    ...(song.artistCredits.length > 0 ? [{ label: 'Artists', value: song.artistCredits.map((credit) => `${credit.creditedName || credit.name} · ${credit.role}`).join(', ') }] : []),
    ...song.sourceIdentities.map((identity) => ({ label: identity.source === 'musicbrainz' ? 'MusicBrainz recording' : identity.source.toUpperCase(), value: identity.externalId })),
    { label: 'Identity', value: metadata.isObservation ? 'Unresolved library observation' : 'Canonical track' },
  ];

  return (
    <section className="border-b border-border-subtle py-9 xl:border-b-0">
      <SectionHeader title="Archive metadata" detail="Identifiers and credits Cantaro currently knows for this recording." />
      <dl className="mt-5 divide-y divide-border-subtle border-t border-border-subtle">
        {rows.map((row) => (
          <div key={`${row.label}:${row.value}`} className="grid gap-1 py-4 text-sm sm:grid-cols-[10rem_minmax(0,1fr)] sm:gap-4">
            <dt className="text-content-subtle">{row.label}</dt>
            <dd className="min-w-0 break-words font-semibold text-content">{row.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function RelatedSongsSection({ library, song }: { library: MusicLibraryResponse; song: MusicLibrarySong }) {
  const artist = song.artist?.toLowerCase();
  if (!artist) return null;
  const relatedSongs = library.songs.filter((candidate) => candidate.id !== song.id && candidate.artist?.toLowerCase() === artist).slice(0, 4);
  if (relatedSongs.length === 0) return null;

  return (
    <section className="border-b border-border-subtle py-9 xl:border-b-0">
      <SectionHeader title={`More from ${songArtist(song)}`} />
      <ul className="mt-5 divide-y divide-border-subtle border-t border-border-subtle">
        {relatedSongs.map((relatedSong) => (
          <li key={relatedSong.id}>
            <Link to="/music/songs/$songId" params={{ songId: relatedSong.id }} className="flex items-center gap-3 py-4 focus-visible:outline-2 focus-visible:outline-focus">
              <SongArtwork song={relatedSong} className="h-12 w-12 shrink-0" />
              <span className="min-w-0 flex-1">
                <strong className="block truncate text-sm text-content">{relatedSong.title}</strong>
                <span className="block truncate text-xs text-content-muted">{songArtist(relatedSong)}</span>
              </span>
              <span className="font-mono text-xs text-content-muted">{formatDuration(relatedSong.durationSeconds)}</span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

function MissingSongPage({ library, detail = 'Cantaro could not find this song in the current library snapshot.' }: { library: MusicLibraryResponse; detail?: string }) {
  return (
    <MusicPageShell library={library}>
      <section className="mx-auto grid min-h-[28rem] max-w-2xl content-center gap-5 text-center">
        <div>
          <h1 className="text-2xl font-bold text-content">Song details unavailable</h1>
          <p className="mt-2 text-content-muted">{detail}</p>
        </div>
        <div><Link to="/music/songs" className={actionClassName({ tone: 'secondary' })}><MusicUiIcon name="arrowLeft" className="h-4 w-4" />Back to songs</Link></div>
      </section>
    </MusicPageShell>
  );
}

// fallow-ignore-next-line complexity
export function MusicSongDetailPage({ library, songId }: { library: MusicLibraryResponse; songId: string }) {
  const [canonicalSong, setCanonicalSong] = useState<MusicLibrarySong | undefined>();
  const [canonicalState, setCanonicalState] = useState<'idle' | 'loading' | 'error'>('idle');
  const librarySong = library.songs.find((candidate) => candidate.id === songId);

  useEffect(() => {
    setCanonicalSong(undefined);
    if (librarySong) {
      setCanonicalState('idle');
      return;
    }
    let active = true;
    setCanonicalState('loading');
    void musicLibraryApi.getCanonicalSong(songId)
      .then((song) => { if (active) { setCanonicalSong(song); setCanonicalState('idle'); } })
      .catch(() => { if (active) setCanonicalState('error'); });
    return () => { active = false; };
  }, [librarySong, songId]);

  const song = librarySong ?? canonicalSong;
  if (!song && canonicalState === 'loading') {
    return <MusicPageShell library={library}><div className="mx-auto min-h-[34rem] max-w-6xl animate-pulse bg-surface-subtle" aria-label="Loading song details" aria-busy="true" /></MusicPageShell>;
  }
  if (!song) {
    return <MissingSongPage library={library} detail={canonicalState === 'error' ? 'Cantaro could not load this canonical song. Check the connection and try again.' : undefined} />;
  }

  const metadata = getSongMetadata(song);
  return (
    <MusicPageShell library={library}>
      <div className="mx-auto max-w-360">
        <Link to="/music/songs" className="mb-5 inline-flex min-h-11 items-center gap-2 text-sm font-semibold text-content-muted transition-colors hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus">
          <MusicUiIcon name="arrowLeft" className="h-4 w-4" />Back to songs
        </Link>
        <SongDetailHero song={song} metadata={metadata} />
        <div className="px-4 sm:px-7 md:px-9">
          <div className="grid gap-x-12 xl:grid-cols-[minmax(0,1.55fr)_minmax(19rem,0.75fr)]">
            <main className="min-w-0">
              <LyricsSection song={song} />
              <PlatformAvailabilitySection song={song} />
              <PlaylistAppearancesSection song={song} library={library} />
            </main>
            <aside className="min-w-0">
              <ArchiveMetadataSection song={song} metadata={metadata} />
              <RelatedSongsSection library={library} song={song} />
            </aside>
          </div>
        </div>
      </div>
    </MusicPageShell>
  );
}
