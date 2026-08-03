import type { MusicLibraryResponse, MusicLibrarySong } from '@cantaro/client-shared/music';
import { useEffect, useMemo, useRef, useState } from 'react';
import { loadExtensionMusicLibrary, recognizeActiveYouTube, type MusicRecognitionResult } from '../../../lib/musicLibrary';
import { MUSIC_CONTEXT_STORAGE_KEY, readActiveTabMusicContext, resolveMusicContext, type BrowserMusicContext } from '../../../lib/musicContext';
import { readExtensionConfig } from '../../../lib/extensionRuntimeConfig';
import { SongDetail } from './SongDetail';

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

  if (!configured) return <State title="Your music, in reach" detail="Sign in to search Cantaro and recognize the song playing in this tab."><button className="mt-4 rounded-xl bg-action px-4 py-2 text-sm font-bold text-action-content transition hover:bg-action-hover" onClick={onSignIn} disabled={isSigningIn}>{isSigningIn ? 'Signing in…' : 'Sign in'}</button></State>;
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
    <label className="block"><span className="sr-only">Search music</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search songs, artists, albums…" className="w-full rounded-xl border border-border-subtle bg-surface px-3 py-2.5 text-sm text-content outline-none focus:border-focus" /></label>
    {library.songs.length === 0 ? <State title="No songs yet" detail="Sync a playlist to build your canonical music library." /> : results.length === 0 ? <State title="No matches" detail="Try another title, artist, or album." /> : <ul className="space-y-1.5">{results.map((song) => <li key={song.id}><button className="group flex w-full items-center gap-3 rounded-xl border border-transparent bg-surface-translucent p-2 text-left transition hover:-translate-y-px hover:border-border-strong hover:bg-surface-hover hover:shadow-sm focus-visible:border-focus focus-visible:ring-2 focus-visible:ring-accent-soft focus-visible:outline-none" onClick={() => setRoute({ kind: 'song', song })}>{song.thumbnailUrl ? <img src={song.thumbnailUrl} alt="" className="size-11 rounded-lg object-cover transition group-hover:scale-[1.03]" /> : <div className="size-11 rounded-lg bg-accent-soft" />}<span className="min-w-0 flex-1"><span className="block truncate text-sm font-bold text-content group-hover:text-content">{song.title}</span><span className="block truncate text-xs text-content-muted">{song.artist || 'Unknown artist'} · {song.playlists.length} playlist{song.playlists.length === 1 ? '' : 's'}</span></span><span className="translate-x-1 text-lg text-accent opacity-0 transition group-hover:translate-x-0 group-hover:opacity-100" aria-hidden>›</span></button></li>)}</ul>}
  </section>;
}

// This compact state renderer intentionally keeps the mutually exclusive recognition outcomes together.
// fallow-ignore-next-line complexity
function RecognitionNotice({ loading, error, recognition, onRetry }: { loading: boolean; error: string | null; recognition: MusicRecognitionResult | null; onRetry: () => void }) {
  if (loading) return <p className="mt-1 text-xs text-slate-300">Checking whether this page is music…</p>;
  if (recognition?.classification === 'music') return <div className="mt-1 flex flex-wrap items-end gap-3"><div className="min-w-48 flex-1"><p className="text-xs text-slate-300">{recognition.status === 'pending' ? 'Cantaro is matching this song in the background. You can close the popup.' : 'Cantaro recognized a song, but could not match it automatically.'}</p>{recognition.title ? <p className="mt-1 truncate text-xs font-semibold text-violet-200">{recognition.title}{recognition.artist ? ` · ${recognition.artist}` : ''}</p> : null}</div><button className="ml-auto rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => void openCantaroPage('/music/matching')}>Review matching</button></div>;
  if (recognition?.classification === 'not_music') return <p className="mt-1 text-xs text-slate-300">This YouTube page is not categorized as music, so Cantaro left it alone.</p>;
  return <div className="mt-1 flex flex-wrap items-end gap-3"><div className="min-w-48 flex-1"><p className="text-xs text-slate-300">{error ?? 'Cantaro could not read this video’s music category from YouTube.'}</p><p className="mt-1 text-[11px] text-slate-400">Reconnect YouTube, then try checking the page again.</p></div><div className="ml-auto flex flex-wrap justify-end gap-2"><button className="rounded-lg border border-white/25 px-3 py-1.5 text-xs font-bold text-white hover:bg-white/10" onClick={onRetry}>Try again</button><button className="rounded-lg bg-white px-3 py-1.5 text-xs font-bold text-slate-950" onClick={() => void openCantaroPage('/music/platforms/youtube')}>Reconnect YouTube</button></div></div>;
}

function State({ title, detail, children }: { title: string; detail: string; children?: React.ReactNode }) { return <div className="m-1 rounded-2xl bg-surface-translucent p-5 text-center"><h2 className="text-base font-black">{title}</h2><p className="mt-1 text-sm text-content-muted">{detail}</p>{children}</div>; }
