import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { useEffect, useRef, useState } from 'react';
import {
  addCanonicalSongToPlaylist,
  removeCanonicalSongFromPlaylist,
} from '../../../lib/musicLibrary';
import { displayLyricsText, loadExtensionLyrics, type LyricsResult } from '../../../lib/lyrics';

type Playlist = MusicLibraryResponse['playlists'][number];
type PlaylistMembership = MusicLibrarySong['playlists'][number];

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function selectedYouTubeVersion(youtubeIds: string[], selectedId: string): string | undefined {
  if (youtubeIds.length <= 1) return selectedId || undefined;
  if (selectedId) return selectedId;
  throw new Error('Choose which YouTube version to sync.');
}

async function addPlaylistMembership(
  trackId: string,
  playlist: Playlist,
  youtubeIds: string[],
  selectedYouTubeId: string,
): Promise<PlaylistMembership> {
  await addCanonicalSongToPlaylist(
    trackId,
    playlist.id,
    selectedYouTubeVersion(youtubeIds, selectedYouTubeId),
  );
  return {
    playlistId: playlist.id,
    playlistName: playlist.name,
    position: playlist.entryCount,
  };
}

async function removePlaylistMembership(
  trackId: string,
  playlistId: string,
  selectedYouTubeId: string,
): Promise<void> {
  await removeCanonicalSongFromPlaylist(trackId, playlistId, selectedYouTubeId || undefined);
}

function usePlaylistController(song: MusicLibrarySong, playlists: MusicLibraryResponse['playlists']) {
  const [memberships, setMemberships] = useState(song.playlists);
  const [busy, setBusy] = useState<string | null>(null);
  const [showPicker, setShowPicker] = useState(false);
  const [confirmingRemoval, setConfirmingRemoval] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const youtubeIds = song.sourceIdentities.filter((identity) => identity.source === 'youtube').map((identity) => identity.externalId);
  const [selectedYouTubeId, setSelectedYouTubeId] = useState(youtubeIds.length === 1 ? youtubeIds[0] : '');
  const trackId = song.id.startsWith('track:') ? song.id.slice(6) : null;
  const availablePlaylists = playlists.filter((playlist) => !memberships.some((membership) => membership.playlistId === playlist.id));

  const addToPlaylist = async (playlist: Playlist) => {
    if (!trackId) return;
    setBusy(playlist.id);
    setMessage(null);
    try {
      const membership = await addPlaylistMembership(trackId, playlist, youtubeIds, selectedYouTubeId);
      setMemberships((current) => [...current, membership]);
      setMessage(`Added to ${playlist.name} and synced.`);
      setShowPicker(false);
    } catch (error) {
      setMessage(errorMessage(error, 'Could not add song.'));
    } finally {
      setBusy(null);
    }
  };

  const removeFromPlaylist = async (playlistId: string) => {
    if (!trackId) return;
    const playlistName = memberships.find((item) => item.playlistId === playlistId)?.playlistName ?? 'playlist';
    setBusy(playlistId);
    setMessage(null);
    try {
      await removePlaylistMembership(trackId, playlistId, selectedYouTubeId);
      setMemberships((current) => current.filter((item) => item.playlistId !== playlistId));
      setMessage(`Removed from ${playlistName} and synced.`);
      setConfirmingRemoval(null);
    } catch (error) {
      setMessage(errorMessage(error, 'Could not remove song.'));
    } finally {
      setBusy(null);
    }
  };

  return {
    memberships,
    busy,
    showPicker,
    setShowPicker,
    confirmingRemoval,
    setConfirmingRemoval,
    message,
    youtubeIds,
    selectedYouTubeId,
    setSelectedYouTubeId,
    trackId,
    availablePlaylists,
    addToPlaylist,
    removeFromPlaylist,
  };
}

type PlaylistController = ReturnType<typeof usePlaylistController>;

export function SongDetail({ song, playlists, onBack }: { song: MusicLibrarySong; playlists: MusicLibraryResponse['playlists']; onBack: () => void }) {
  const controller = usePlaylistController(song, playlists);

  return (
    <article className="space-y-3 p-1">
      <button onClick={onBack} className="rounded-lg px-2 py-1 text-xs font-bold text-accent-strong hover:bg-surface-hover">← Back to music</button>
      <SongSummary song={song} />
      <PlatformLinksSection song={song} />
      <LyricsSection trackId={controller.trackId} />
      <IdentifiersSection song={song} />
      <YouTubeVersionSection controller={controller} />
      <PlaylistSection controller={controller} />
    </article>
  );
}

function SongSummary({ song }: { song: MusicLibrarySong }) {
  const duration = song.durationSeconds
    ? ` · ${Math.floor(song.durationSeconds / 60)}:${String(song.durationSeconds % 60).padStart(2, '0')}`
    : '';
  return <div className="flex gap-3 rounded-2xl bg-surface p-3">{song.thumbnailUrl ? <img src={song.thumbnailUrl} alt="" className="size-20 rounded-xl object-cover" /> : <div className="size-20 rounded-xl bg-accent-soft" />}<div className="min-w-0"><h2 className="text-lg font-black">{song.title}</h2><p className="text-sm text-content-muted">{song.artist || 'Unknown artist'}</p><p className="mt-1 text-xs text-content-muted">{song.albums.join(', ') || 'No album'}{duration}</p></div></div>;
}

function PlatformLinksSection({ song }: { song: MusicLibrarySong }) {
  if (song.platformLinks.length === 0) return null;
  return <Section title="Listen"><div className="flex flex-wrap gap-2">{song.platformLinks.map((link) => <a key={`${link.platform}-${link.url}`} href={link.url} target="_blank" rel="noreferrer" className="rounded-lg bg-slate-950 px-3 py-1.5 text-xs font-bold text-white">{link.label}</a>)}</div></Section>;
}

function IdentifiersSection({ song }: { song: MusicLibrarySong }) {
  if (song.sourceIdentities.length === 0) return null;
  return <Section title="Identifiers">{song.sourceIdentities.map((identity) => <p key={`${identity.source}-${identity.externalId}`} className="truncate text-xs text-content-muted"><b>{identity.source}:</b> {identity.externalId}</p>)}</Section>;
}

function YouTubeVersionSection({ controller }: { controller: PlaylistController }) {
  if (controller.youtubeIds.length <= 1) return null;
  return <Section title="YouTube version"><select value={controller.selectedYouTubeId} onChange={(event) => controller.setSelectedYouTubeId(event.target.value)} className="w-full rounded-lg border border-border-strong bg-surface px-2 py-1.5 text-xs text-content"><option value="">Choose a version to sync…</option>{controller.youtubeIds.map((id) => <option key={id} value={id}>{id}</option>)}</select></Section>;
}

function PlaylistSection({ controller }: { controller: PlaylistController }) {
  return (
    <Section title="Playlists">
      <PlaylistHeader controller={controller} />
      <MembershipList controller={controller} />
      <PlaylistPicker controller={controller} />
      {controller.message ? <p className="mt-2 text-xs text-content-muted" role="status">{controller.message}</p> : null}
    </Section>
  );
}

function PlaylistHeader({ controller }: { controller: PlaylistController }) {
  const canAdd = Boolean(controller.trackId && controller.availablePlaylists.length);
  return <div className="mb-2 flex items-center justify-between gap-3"><p className="text-xs text-content-muted">Tracked playlist memberships</p>{canAdd ? <button onClick={() => controller.setShowPicker((value) => !value)} className="flex size-7 items-center justify-center rounded-full bg-accent-soft text-lg font-bold text-accent-strong hover:bg-surface-hover" aria-label="Add to another playlist">+</button> : null}</div>;
}

function MembershipList({ controller }: { controller: PlaylistController }) {
  if (controller.memberships.length === 0) {
    return <p className="text-xs text-content-muted">Not currently in a playlist.</p>;
  }
  return <div className="space-y-1">{controller.memberships.map((playlist) => <MembershipRow key={playlist.playlistId} membership={playlist} controller={controller} />)}</div>;
}

function MembershipRow({ membership, controller }: { membership: PlaylistMembership; controller: PlaylistController }) {
  const confirming = controller.confirmingRemoval === membership.playlistId;
  return <div className="group flex min-h-9 items-center gap-2 rounded-lg px-2 text-sm text-content hover:bg-danger-surface"><span className="min-w-0 flex-1 truncate">{membership.playlistName} <span className="text-xs text-content-subtle">#{membership.position + 1}</span></span>{confirming ? <div className="flex items-center gap-1"><span className="text-[11px] font-semibold text-rose-700">Remove?</span><button disabled={controller.busy !== null} onClick={() => void controller.removeFromPlaylist(membership.playlistId)} className="rounded bg-rose-700 px-2 py-1 text-[11px] font-bold text-white">Confirm</button><button onClick={() => controller.setConfirmingRemoval(null)} className="rounded px-2 py-1 text-[11px] font-bold text-content-muted">Cancel</button></div> : <button onClick={() => controller.setConfirmingRemoval(membership.playlistId)} className="flex size-7 items-center justify-center rounded-md text-rose-600 opacity-0 transition hover:bg-rose-100 group-hover:opacity-100 focus:opacity-100" aria-label={`Remove from ${membership.playlistName}`}><span aria-hidden>🗑</span></button>}</div>;
}

function PlaylistPicker({ controller }: { controller: PlaylistController }) {
  if (!controller.showPicker) return null;
  return <div className="mt-3 flex flex-wrap gap-2 border-t border-border-subtle pt-3">{controller.availablePlaylists.map((playlist) => <button key={playlist.id} disabled={controller.busy !== null} onClick={() => void controller.addToPlaylist(playlist)} className="rounded-lg bg-accent-soft px-3 py-1.5 text-xs font-bold text-accent-strong hover:bg-surface-hover disabled:opacity-50">{controller.busy === playlist.id ? 'Adding…' : playlist.name}</button>)}</div>;
}

type LyricsView =
  | { kind: 'loading' }
  | { kind: 'ready'; result: LyricsResult }
  | { kind: 'error'; message: string }
  | { kind: 'unavailable'; message: string };

function LyricsSection({ trackId }: { trackId: string | null }) {
  const [view, setView] = useState<LyricsView>(trackId ? { kind: 'loading' } : { kind: 'unavailable', message: 'Lyrics are not available for this song.' });
  const requestId = useRef(0);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    const controller = new AbortController();

    if (!trackId) {
      setView({ kind: 'unavailable', message: 'Lyrics are not available for this song.' });
      return () => controller.abort();
    }

    setView({ kind: 'loading' });
    void loadExtensionLyrics(trackId, controller.signal)
      .then((result) => {
        if (requestId.current === currentRequest) setView({ kind: 'ready', result });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || requestId.current !== currentRequest) return;
        setView({ kind: 'error', message: error instanceof Error ? error.message : 'The lyrics provider could not be reached.' });
      });

    return () => controller.abort();
  }, [trackId]);

  const content = view.kind === 'loading'
    ? <LyricsLoading />
    : view.kind === 'ready'
      ? <LyricsResultContent result={view.result} />
      : <LyricsNoticeContent title={view.kind === 'error' ? 'Lyrics provider unavailable' : 'Lyrics unavailable'} detail={view.message} />;

  return <Section title="Lyrics">{content}</Section>;
}

function LyricsLoading() {
  return <div className="space-y-2" aria-label="Loading lyrics"><div className="cantaro-lyrics-loading-line h-3 w-11/12 rounded" /><div className="cantaro-lyrics-loading-line h-3 w-4/5 rounded" /><div className="cantaro-lyrics-loading-line h-3 w-9/12 rounded" /></div>;
}

const lyricsNotices = {
  available: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
  instrumental: { title: 'Instrumental track', fallback: 'This track has no lyrics.' },
  ambiguous: { title: 'Lyrics need review', fallback: 'Cantaro found more than one possible lyrics match, so it did not choose one.' },
  provider_error: { title: 'Lyrics provider unavailable', fallback: 'Try again in a moment.' },
  disabled: { title: 'Lyrics are unavailable', fallback: 'Lyrics lookup is disabled for this Cantaro server.' },
  unavailable: { title: 'Lyrics unavailable', fallback: 'No lyrics were found for this song.' },
} satisfies Record<LyricsResult['state'], { title: string; fallback: string }>;

function LyricsResultContent({ result }: { result: LyricsResult }) {
  const selected = result.state === 'available' ? displayLyricsText(result) : null;
  if (!selected) {
    const notice = lyricsNotices[result.state];
    return <LyricsNoticeContent title={notice.title} detail={result.explanation || notice.fallback} attribution={result.attribution} />;
  }

  return <><div className="flex items-baseline justify-between gap-3"><p className="text-xs font-semibold text-content">{selected.synchronized ? 'Timed lyrics' : 'Lyrics'}</p><span className="text-[11px] text-content-muted">{result.matchStatus === 'exact' ? 'Matched' : 'Best match'}</span></div><pre className="mt-2 whitespace-pre-wrap break-words font-sans text-sm leading-6 text-content">{selected.text}</pre><p className="mt-3 text-[11px] text-content-muted">{result.attribution}</p></>;
}

function LyricsNoticeContent({ title, detail, attribution }: { title: string; detail: string; attribution?: string }) {
  return <><p className="text-sm font-bold text-content">{title}</p><p className="mt-1 text-xs leading-5 text-content-muted">{detail}</p>{attribution ? <p className="mt-3 text-[11px] text-content-muted">{attribution}</p> : null}</>;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return <section className="rounded-2xl bg-surface-translucent p-3"><h3 className="mb-2 text-xs font-black tracking-wider text-content-muted uppercase">{title}</h3>{children}</section>;
}
