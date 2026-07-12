import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { useEffect, useMemo, useRef, useState } from 'react';
import { addCanonicalSongToPlaylist, loadExtensionMusicLibrary, recognizeActiveYouTube, removeCanonicalSongFromPlaylist, type MusicRecognitionResult } from '../../../lib/musicLibrary';
import { MUSIC_CONTEXT_STORAGE_KEY, readActiveTabMusicContext, resolveMusicContext, type BrowserMusicContext } from '../../../lib/musicContext';
import { readExtensionConfig } from '../../../lib/extensionRuntimeConfig';
import { displayLyricsText, loadExtensionLyrics, type LyricsResult } from '../../../lib/lyrics';

type Route = { kind: 'home' } | { kind: 'song'; song: MusicLibrarySong };

async function openSongInCantaro(songId: string) {
  const config = await readExtensionConfig();
  await browser.tabs.create({ url: `${config.webBaseUrl}/music/songs/${encodeURIComponent(songId)}` });
}

async function openCantaroPage(path: string) {
  const config = await readExtensionConfig();
  await browser.tabs.create({ url: `${config.webBaseUrl}${path}` });
}

// The component owns a deliberately small popup state machine: auth, load, search, context and detail.
// fallow-ignore-next-line complexity
export function MusicLibrary({ configured, onSignIn, isSigningIn }: { configured: boolean; onSignIn: () => void; isSigningIn: boolean }) {
  const [library, setLibrary] = useState<MusicLibraryResponse | null>(null);
  const [context, setContext] = useState<BrowserMusicContext | null>(null);
  const [recognition, setRecognition] = useState<MusicRecognitionResult | null>(null);
  const [recognitionLoading, setRecognitionLoading] = useState(false);
  const [recognitionError, setRecognitionError] = useState<string | null>(null);
  const [recognitionRetry, setRecognitionRetry] = useState(0);
  const [route, setRoute] = useState<Route>({ kind: 'home' });
  const [query, setQuery] = useState('');
  const [error, setError] = useState<string | null>(null);
  const recognitionRequest = useRef(0);

  useEffect(() => {
    if (!configured) return;
    let active = true;
    void Promise.all([
      loadExtensionMusicLibrary(),
      readActiveTabMusicContext(),
    ]).then(([nextLibrary, activeContext]) => {
      if (!active) return;
      setLibrary(nextLibrary);
      setContext(activeContext);
    }).catch((reason: unknown) => active && setError(reason instanceof Error ? reason.message : 'Could not load music.'));
    return () => { active = false; };
  }, [configured]);

  useEffect(() => {
    const requestId = ++recognitionRequest.current;
    const controller = new AbortController();
    setRecognition(null);
    setRecognitionError(null);
    if (!configured || !context) return () => controller.abort();
    setRecognitionLoading(true);
    void recognizeActiveYouTube(context, controller.signal)
      .then((result) => {
        if (recognitionRequest.current === requestId) setRecognition(result);
      })
      .catch((reason: unknown) => {
        if (!(reason instanceof DOMException && reason.name === 'AbortError') && recognitionRequest.current === requestId)
          setRecognitionError(reason instanceof Error ? reason.message : 'Cantaro could not check this page.');
      })
      .finally(() => {
        if (recognitionRequest.current === requestId) setRecognitionLoading(false);
      });
    return () => controller.abort();
  }, [configured, context?.externalId, context?.site, recognitionRetry]);

  useEffect(() => {
    const handleStorageChange = (changes: Record<string, Browser.storage.StorageChange>) => {
      if (changes[MUSIC_CONTEXT_STORAGE_KEY]?.newValue !== undefined)
        void readActiveTabMusicContext().then(setContext);
    };
    browser.storage.local.onChanged.addListener(handleStorageChange);
    return () => browser.storage.local.onChanged.removeListener(handleStorageChange);
  }, []);

  const results = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!library) return [];
    return library.songs.filter((song) => !term || `${song.title} ${song.artist ?? ''} ${song.albums.join(' ')}`.toLowerCase().includes(term)).slice(0, 40);
  }, [library, query]);
  const resolution = context && library ? resolveMusicContext(context, library.songs) : null;

  if (!configured) return <State title="Your music, in reach" detail="Sign in to search Cantaro and recognize the song playing in this tab."><button className="mt-4 rounded-xl bg-violet-600 px-4 py-2 text-sm font-bold text-white" onClick={onSignIn} disabled={isSigningIn}>{isSigningIn ? 'Signing in…' : 'Sign in'}</button></State>;
  if (error) return <State title="Music is unavailable" detail={error} />;
  if (!library) return <State title="Loading your library…" detail="Fetching canonical songs and playlists." />;
  if (route.kind === 'song') return <SongDetail song={route.song} playlists={library.playlists} onBack={() => setRoute({ kind: 'home' })} />;

  return <section className="space-y-3 p-1">
    {context ? <div className="rounded-xl bg-slate-950 p-3 text-white">
      <p className="text-[10px] font-black tracking-widest text-violet-300 uppercase">Current tab</p>
      {resolution?.status === 'matched' ? <div className="mt-1 flex flex-wrap items-end gap-3"><div className="min-w-48 flex-1"><p className="truncate text-sm font-bold">{resolution.song.title}</p><p className="mt-0.5 truncate text-xs font-semibold text-violet-200">{resolution.song.artist || 'Unknown artist'}</p></div><div className="ml-auto flex flex-wrap justify-end gap-2"><button className="rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => setRoute({ kind: 'song', song: resolution.song })}>View</button><button className="rounded-lg border border-white/25 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/10" onClick={() => void openSongInCantaro(resolution.song.id)}>Open in Cantaro</button></div></div>
        : recognition?.status === 'matched' && recognition.song ? <div className="mt-1 flex flex-wrap items-end gap-3"><div className="min-w-48 flex-1"><p className="truncate text-sm font-bold">{recognition.song.title}</p><p className="mt-0.5 truncate text-xs font-semibold text-violet-200">{recognition.song.artist || 'Unknown artist'}</p><p className="mt-0.5 text-xs text-slate-300">Known to Cantaro{recognition.inUserLibrary ? '' : ' · not in your playlists yet'}</p></div><div className="ml-auto flex flex-wrap justify-end gap-2"><button className="rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => setRoute({ kind: 'song', song: recognition.song! })}>View</button><button className="rounded-lg border border-white/25 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/10" onClick={() => void openSongInCantaro(recognition.song!.id)}>Open in Cantaro</button></div></div>
          : <RecognitionNotice loading={recognitionLoading} error={recognitionError} recognition={recognition} onRetry={() => setRecognitionRetry((value) => value + 1)} />}
    </div> : null}
    <label className="block"><span className="sr-only">Search music</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search songs, artists, albums…" className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm outline-none focus:border-violet-500" /></label>
    {library.songs.length === 0 ? <State title="No songs yet" detail="Sync a playlist to build your canonical music library." /> : results.length === 0 ? <State title="No matches" detail="Try another title, artist, or album." /> : <ul className="space-y-1.5">{results.map((song) => <li key={song.id}><button className="group flex w-full items-center gap-3 rounded-xl border border-transparent bg-white/65 p-2 text-left transition hover:-translate-y-px hover:border-violet-200 hover:bg-violet-50 hover:shadow-sm focus-visible:border-violet-400 focus-visible:ring-2 focus-visible:ring-violet-100 focus-visible:outline-none" onClick={() => setRoute({ kind: 'song', song })}>{song.thumbnailUrl ? <img src={song.thumbnailUrl} alt="" className="size-11 rounded-lg object-cover transition group-hover:scale-[1.03]" /> : <div className="size-11 rounded-lg bg-violet-100" />}<span className="min-w-0 flex-1"><span className="block truncate text-sm font-bold text-slate-800 group-hover:text-violet-950">{song.title}</span><span className="block truncate text-xs text-slate-500">{song.artist || 'Unknown artist'} · {song.playlists.length} playlist{song.playlists.length === 1 ? '' : 's'}</span></span><span className="translate-x-1 text-lg text-violet-500 opacity-0 transition group-hover:translate-x-0 group-hover:opacity-100" aria-hidden>›</span></button></li>)}</ul>}
  </section>;
}

// This compact state renderer intentionally keeps the mutually exclusive recognition outcomes together.
// fallow-ignore-next-line complexity
function RecognitionNotice({ loading, error, recognition, onRetry }: { loading: boolean; error: string | null; recognition: MusicRecognitionResult | null; onRetry: () => void }) {
  if (loading) return <p className="mt-1 text-xs text-slate-300">Checking whether this page is music…</p>;
  if (recognition?.classification === 'music') return <div className="mt-1 flex flex-wrap items-end gap-3"><div className="min-w-48 flex-1"><p className="text-xs text-slate-300">Cantaro recognized a song, but could not match it automatically.</p>{recognition.title ? <p className="mt-1 truncate text-xs font-semibold text-violet-200">{recognition.title}{recognition.artist ? ` · ${recognition.artist}` : ''}</p> : null}</div><button className="ml-auto rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => void openCantaroPage('/music/matching')}>Review matching</button></div>;
  if (recognition?.classification === 'not_music') return <p className="mt-1 text-xs text-slate-300">This YouTube page is not categorized as music, so Cantaro left it alone.</p>;
  return <div className="mt-1 flex flex-wrap items-end gap-3"><div className="min-w-48 flex-1"><p className="text-xs text-slate-300">{error ?? 'Cantaro could not read this video’s music category from YouTube.'}</p><p className="mt-1 text-[11px] text-slate-400">Reconnect YouTube, then try checking the page again.</p></div><div className="ml-auto flex flex-wrap justify-end gap-2"><button className="rounded-lg border border-white/25 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/10" onClick={onRetry}>Try again</button><button className="rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => void openCantaroPage('/music/platforms/youtube')}>Reconnect YouTube</button></div></div>;
}

// fallow-ignore-next-line complexity
function SongDetail({ song, playlists, onBack }: { song: MusicLibrarySong; playlists: MusicLibraryResponse['playlists']; onBack: () => void }) {
  const [memberships, setMemberships] = useState(song.playlists);
  const [busy, setBusy] = useState<string | null>(null);
  const [showPicker, setShowPicker] = useState(false);
  const [confirmingRemoval, setConfirmingRemoval] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const youtubeIds = song.sourceIdentities.filter((identity) => identity.source === 'youtube').map((identity) => identity.externalId);
  const [selectedYouTubeId, setSelectedYouTubeId] = useState(youtubeIds.length === 1 ? youtubeIds[0] : '');
  const trackId = song.id.startsWith('track:') ? song.id.slice(6) : null;
  const availablePlaylists = playlists.filter((playlist) => !memberships.some((membership) => membership.playlistId === playlist.id));
  const addToPlaylist = async (playlist: MusicLibraryResponse['playlists'][number]) => {
    if (!trackId) return;
    setBusy(playlist.id); setMessage(null);
    try {
      if (youtubeIds.length > 1 && !selectedYouTubeId) throw new Error('Choose which YouTube version to sync.');
      await addCanonicalSongToPlaylist(trackId, playlist.id, selectedYouTubeId || undefined);
      setMemberships((current) => [...current, { playlistId: playlist.id, playlistName: playlist.name, position: playlist.entryCount }]);
      setMessage(`Added to ${playlist.name} and synced.`); setShowPicker(false);
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not add song.'); }
    finally { setBusy(null); }
  };
  const removeFromPlaylist = async (playlistId: string) => {
    if (!trackId) return;
    const membership = memberships.find((item) => item.playlistId === playlistId);
    setBusy(playlistId); setMessage(null);
    try {
      await removeCanonicalSongFromPlaylist(trackId, playlistId, selectedYouTubeId || undefined);
      setMemberships((current) => current.filter((item) => item.playlistId !== playlistId));
      setMessage(`Removed from ${membership?.playlistName ?? 'playlist'} and synced.`); setConfirmingRemoval(null);
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not remove song.'); }
    finally { setBusy(null); }
  };
  return <article className="space-y-3 p-1"><button onClick={onBack} className="rounded-lg px-2 py-1 text-xs font-bold text-violet-700 hover:bg-violet-50">← Back to music</button><div className="flex gap-3 rounded-2xl bg-white p-3">{song.thumbnailUrl ? <img src={song.thumbnailUrl} alt="" className="size-20 rounded-xl object-cover" /> : <div className="size-20 rounded-xl bg-violet-100" />}<div className="min-w-0"><h2 className="text-lg font-black">{song.title}</h2><p className="text-sm text-slate-600">{song.artist || 'Unknown artist'}</p><p className="mt-1 text-xs text-slate-500">{song.albums.join(', ') || 'No album'}{song.durationSeconds ? ` · ${Math.floor(song.durationSeconds / 60)}:${String(song.durationSeconds % 60).padStart(2, '0')}` : ''}</p></div></div>
    {song.platformLinks.length > 0 ? <Section title="Listen"><div className="flex flex-wrap gap-2">{song.platformLinks.map((link) => <a key={`${link.platform}-${link.url}`} href={link.url} target="_blank" rel="noreferrer" className="rounded-lg bg-slate-950 px-3 py-1.5 text-xs font-bold text-white">{link.label}</a>)}</div></Section> : null}
    <LyricsSection trackId={trackId} />
    {song.sourceIdentities.length > 0 ? <Section title="Identifiers">{song.sourceIdentities.map((identity) => <p key={`${identity.source}-${identity.externalId}`} className="truncate text-xs text-slate-600"><b>{identity.source}:</b> {identity.externalId}</p>)}</Section> : null}
    {youtubeIds.length > 1 ? <Section title="YouTube version"><select value={selectedYouTubeId} onChange={(event) => setSelectedYouTubeId(event.target.value)} className="w-full rounded-lg border border-violet-200 bg-white px-2 py-1.5 text-xs"><option value="">Choose a version to sync…</option>{youtubeIds.map((id) => <option key={id} value={id}>{id}</option>)}</select></Section> : null}
    <Section title="Playlists"><div className="mb-2 flex items-center justify-between gap-3"><p className="text-xs text-slate-500">Tracked playlist memberships</p>{trackId && availablePlaylists.length ? <button onClick={() => setShowPicker((value) => !value)} className="flex size-7 items-center justify-center rounded-full bg-violet-100 text-lg font-bold text-violet-800 hover:bg-violet-200" aria-label="Add to another playlist">+</button> : null}</div>{memberships.length ? <div className="space-y-1">{memberships.map((playlist) => <div key={playlist.playlistId} className="group flex min-h-9 items-center gap-2 rounded-lg px-2 text-sm text-slate-700 hover:bg-rose-50"><span className="min-w-0 flex-1 truncate">{playlist.playlistName} <span className="text-xs text-slate-400">#{playlist.position + 1}</span></span>{confirmingRemoval === playlist.playlistId ? <div className="flex items-center gap-1"><span className="text-[11px] font-semibold text-rose-700">Remove?</span><button disabled={busy !== null} onClick={() => void removeFromPlaylist(playlist.playlistId)} className="rounded bg-rose-700 px-2 py-1 text-[11px] font-bold text-white">Confirm</button><button onClick={() => setConfirmingRemoval(null)} className="rounded px-2 py-1 text-[11px] font-bold text-slate-600">Cancel</button></div> : <button onClick={() => setConfirmingRemoval(playlist.playlistId)} className="flex size-7 items-center justify-center rounded-md text-rose-600 opacity-0 transition hover:bg-rose-100 group-hover:opacity-100 focus:opacity-100" aria-label={`Remove from ${playlist.playlistName}`}><span aria-hidden>🗑</span></button>}</div>)}</div> : <p className="text-xs text-slate-500">Not currently in a playlist.</p>}{showPicker ? <div className="mt-3 flex flex-wrap gap-2 border-t border-violet-100 pt-3">{availablePlaylists.map((playlist) => <button key={playlist.id} disabled={busy !== null} onClick={() => void addToPlaylist(playlist)} className="rounded-lg bg-violet-100 px-3 py-1.5 text-xs font-bold text-violet-800 hover:bg-violet-200 disabled:opacity-50">{busy === playlist.id ? 'Adding…' : playlist.name}</button>)}</div> : null}{message ? <p className="mt-2 text-xs text-slate-600" role="status">{message}</p> : null}</Section>
  </article>;
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

  return <><div className="flex items-baseline justify-between gap-3"><p className="text-xs font-semibold text-slate-700">{selected.synchronized ? 'Timed lyrics' : 'Lyrics'}</p><span className="text-[11px] text-slate-500">{result.matchStatus === 'exact' ? 'Matched' : 'Best match'}</span></div><pre className="mt-2 whitespace-pre-wrap break-words font-sans text-sm leading-6 text-slate-700">{selected.text}</pre><p className="mt-3 text-[11px] text-slate-500">{result.attribution}</p></>;
}

function LyricsNoticeContent({ title, detail, attribution }: { title: string; detail: string; attribution?: string }) {
  return <><p className="text-sm font-bold text-slate-800">{title}</p><p className="mt-1 text-xs leading-5 text-slate-600">{detail}</p>{attribution ? <p className="mt-3 text-[11px] text-slate-500">{attribution}</p> : null}</>;
}
function Section({ title, children }: { title: string; children: React.ReactNode }) { return <section className="rounded-2xl bg-white/80 p-3"><h3 className="mb-2 text-xs font-black tracking-wider text-slate-500 uppercase">{title}</h3>{children}</section>; }
function State({ title, detail, children }: { title: string; detail: string; children?: React.ReactNode }) { return <div className="m-1 rounded-2xl bg-white/80 p-5 text-center"><h2 className="text-base font-black">{title}</h2><p className="mt-1 text-sm text-slate-500">{detail}</p>{children}</div>; }
