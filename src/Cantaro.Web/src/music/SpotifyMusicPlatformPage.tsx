import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import {
  isPlatformReconnectRequiredError,
  MusicPlatformIcon,
  platformManager,
  type PlatformAccountStatus,
  type PlatformPlaylist,
  type PlatformSong,
} from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicPageShell } from './MusicPageShell';
import { useMusicQueue } from './useMusicQueue';

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

function needsSpotifyReconnect(status: PlatformAccountStatus) {
  return Boolean(status.needsReconnect || status.connectionState === 'reconnect_required');
}

function canBrowseSpotify(status: PlatformAccountStatus) {
  return status.isConnected && !needsSpotifyReconnect(status);
}

function useSpotifyPlaylistBrowser() {
  const [playlists, setPlaylists] = useState<PlatformPlaylist[]>([]);
  const [selectedPlaylist, setSelectedPlaylist] = useState<PlatformPlaylist | null>(null);
  const [songs, setSongs] = useState<PlatformSong[]>([]);
  const [isLoadingSongs, setIsLoadingSongs] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsReconnect, setNeedsReconnect] = useState(false);

  const clearPlaylists = useCallback(() => {
    setPlaylists([]);
    setSelectedPlaylist(null);
    setSongs([]);
  }, []);

  const handleError = useCallback((loadError: unknown, fallback: string) => {
    if (isPlatformReconnectRequiredError(loadError)) {
      setNeedsReconnect(true);
      clearPlaylists();
      setError(null);
      return;
    }
    setError(errorMessage(loadError, fallback));
  }, [clearPlaylists]);

  const loadPlaylists = useCallback(async (forceRefresh = false) => {
    try {
      const loaded = forceRefresh
        ? await platformManager.refreshPlaylists('spotify')
        : await platformManager.playlists('spotify');
      setPlaylists(loaded);
      setNeedsReconnect(false);
      setError(null);
    } catch (loadError) {
      handleError(loadError, 'Could not load Spotify playlists.');
    }
  }, [handleError]);

  const selectPlaylist = useCallback(async (playlist: PlatformPlaylist) => {
    setSelectedPlaylist(playlist);
    setIsLoadingSongs(true);
    try {
      setSongs(await playlist.songs());
      setNeedsReconnect(false);
      setError(null);
    } catch (loadError) {
      setSongs([]);
      handleError(loadError, 'Could not load this Spotify playlist.');
    } finally {
      setIsLoadingSongs(false);
    }
  }, [handleError]);

  const clearSelection = useCallback(() => {
    setSelectedPlaylist(null);
    setSongs([]);
  }, []);
  const refresh = useCallback(() => loadPlaylists(true), [loadPlaylists]);

  return {
    playlists,
    selectedPlaylist,
    songs,
    isLoadingSongs,
    error,
    needsReconnect,
    clearPlaylists,
    loadPlaylists,
    refresh,
    selectPlaylist,
    clearSelection,
  };
}

function useSpotifyConnection(
  loadPlaylists: (forceRefresh?: boolean) => Promise<void>,
  clearPlaylists: () => void,
) {
  const [status, setStatus] = useState<PlatformAccountStatus | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let disposed = false;
    const commit = (action: () => void) => {
      if (!disposed) action();
    };
    const initialize = async () => {
      setIsLoading(true);
      try {
        const loadedStatus = await platformManager.status('spotify');
        if (disposed) return;
        setStatus(loadedStatus);
        if (canBrowseSpotify(loadedStatus)) await loadPlaylists();
      } catch (loadError) {
        commit(() => setError(errorMessage(loadError, 'Could not check the Spotify connection.')));
      } finally {
        commit(() => setIsLoading(false));
      }
    };
    void initialize();
    return () => {
      disposed = true;
    };
  }, [loadPlaylists]);

  const needsReconnect = status ? needsSpotifyReconnect(status) : false;
  const connect = useCallback(() => platformManager.connect('spotify', {
    route: '/music/platforms/spotify',
    trigger: needsReconnect ? 'spotify-page-reconnect' : 'spotify-page-connect',
  }), [needsReconnect]);
  const disconnect = useCallback(async () => {
    try {
      await platformManager.disconnect('spotify');
      setStatus({ platformId: 'spotify', isConnected: false, connectionState: 'disconnected' });
      clearPlaylists();
      setError(null);
    } catch (disconnectError) {
      setError(errorMessage(disconnectError, 'Could not disconnect Spotify. Try again.'));
    }
  }, [clearPlaylists]);

  return { status, isLoading, error, needsReconnect, connect, disconnect };
}

function useSpotifyBrowser() {
  const playlists = useSpotifyPlaylistBrowser();
  const connection = useSpotifyConnection(playlists.loadPlaylists, playlists.clearPlaylists);
  return {
    ...playlists,
    ...connection,
    error: playlists.error ?? connection.error,
    needsReconnect: playlists.needsReconnect || connection.needsReconnect,
  };
}

function SpotifyAttribution({ compact = false }: { compact?: boolean }) {
  return (
    <a
      href="https://open.spotify.com/"
      target="_blank"
      rel="noopener noreferrer"
      className={`inline-flex items-center gap-2 rounded-full bg-slate-950 text-white transition hover:bg-slate-800 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 focus-visible:outline-none ${
        compact ? 'px-3 py-1.5 text-xs' : 'px-4 py-2 text-sm'
      } font-black`}
      aria-label="Open Spotify"
    >
      <MusicPlatformIcon platformId="spotify" className="h-8 w-8 text-[#1ed760]" />
      Spotify
    </a>
  );
}

function SpotifyHeader({
  accountName,
  isConnected,
  needsReconnect,
  playlistCount,
  onConnect,
  onDisconnect,
  onRefresh,
}: {
  accountName?: string;
  isConnected: boolean;
  needsReconnect: boolean;
  playlistCount: number;
  onConnect: () => void;
  onDisconnect: () => void;
  onRefresh: () => void;
}) {
  const description = getSpotifyHeaderDescription({ accountName, isConnected, needsReconnect, playlistCount });

  return (
    <section className="rounded-3xl bg-[#ece9ff] p-6 shadow-[0_28px_90px_rgba(88,74,150,0.12)]">
      <div className="flex flex-wrap items-end justify-between gap-5">
        <div className="max-w-2xl">
          <div className="flex items-center gap-3">
            <span className="inline-flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-950 text-[#1ed760]">
              <MusicPlatformIcon platformId="spotify" className="h-7 w-7" title="Spotify" />
            </span>
            <h1 className="text-4xl leading-tight font-black text-slate-950 sm:text-5xl">Spotify</h1>
          </div>
          <p className="mt-3 text-sm leading-6 font-semibold text-slate-600">{description}</p>
          <p className="mt-2 text-xs font-semibold text-slate-500">Spotify content is shown with links back to Spotify.</p>
        </div>
        <SpotifyHeaderActions
          isConnected={isConnected}
          needsReconnect={needsReconnect}
          onConnect={onConnect}
          onDisconnect={onDisconnect}
          onRefresh={onRefresh}
        />
      </div>
    </section>
  );
}

function getSpotifyHeaderDescription({
  accountName,
  isConnected,
  needsReconnect,
  playlistCount,
}: {
  accountName?: string;
  isConnected: boolean;
  needsReconnect: boolean;
  playlistCount: number;
}) {
  if (needsReconnect) {
    return `Reconnect ${accountName ?? 'your Spotify account'} before Cantaro can refresh its playlists.`;
  }
  if (isConnected) {
    return `${playlistCount.toLocaleString()} playlists from ${accountName ?? 'your Spotify account'} are ready to browse.`;
  }
  return 'Connect Spotify to browse playlists and choose what belongs in your Cantaro archive.';
}

function SpotifyHeaderActions({
  isConnected,
  needsReconnect,
  onConnect,
  onDisconnect,
  onRefresh,
}: {
  isConnected: boolean;
  needsReconnect: boolean;
  onConnect: () => void;
  onDisconnect: () => void;
  onRefresh: () => void;
}) {
  return (
    <div className="flex flex-wrap gap-2">
      {isConnected && !needsReconnect ? (
        <button type="button" className="rounded-2xl bg-white px-5 py-3 text-sm font-black text-slate-800 transition hover:bg-violet-50 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none" onClick={onRefresh}>
          Refresh
        </button>
      ) : (
        <button type="button" className="rounded-2xl bg-violet-600 px-5 py-3 text-sm font-black text-white transition hover:bg-violet-500 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 focus-visible:outline-none" onClick={onConnect}>
          {needsReconnect ? 'Reconnect Spotify' : 'Connect Spotify'}
        </button>
      )}
      {isConnected ? (
        <button type="button" className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white transition hover:bg-slate-800 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 focus-visible:outline-none" onClick={onDisconnect}>
          Disconnect
        </button>
      ) : null}
    </div>
  );
}

function SpotifyPlaylistCard({
  playlist,
  onSelect,
}: {
  playlist: PlatformPlaylist;
  onSelect: (playlist: PlatformPlaylist) => void;
}) {
  return (
    <article className="overflow-hidden rounded-3xl bg-white/70 shadow-[0_16px_45px_rgba(88,74,150,0.08)]">
      <button
        type="button"
        onClick={() => onSelect(playlist)}
        className="group block w-full text-left focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none focus-visible:ring-inset"
      >
        {playlist.thumbnailUrl ? (
          <img src={playlist.thumbnailUrl} alt="" className="aspect-square w-full bg-white object-contain" />
        ) : (
          <span className="flex aspect-square w-full items-center justify-center bg-[#eeeaff] text-slate-500">
            <MusicPlatformIcon platformId="spotify" className="h-10 w-10" />
          </span>
        )}
        <span className="block p-4">
          <span className="block truncate font-black text-slate-950">{playlist.title}</span>
          <span className="mt-1 block text-xs font-semibold text-slate-500">
            {playlist.itemCount.toLocaleString()} tracks{playlist.ownerName ? ` · ${playlist.ownerName}` : ''}
          </span>
        </span>
      </button>
      <div className="flex items-center justify-between gap-3 border-t border-[#ece8fa] px-4 py-3">
        <SpotifyAttribution compact />
        {playlist.externalUrl ? (
          <a
            href={playlist.externalUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-xs font-black text-violet-700 hover:underline focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
          >
            Open playlist
          </a>
        ) : null}
      </div>
    </article>
  );
}

function mapSong(song: PlatformSong): MusicCollectionTrack {
  return {
    id: song.id,
    title: song.title,
    artist: song.artistName,
    albums: song.albumName ? [song.albumName] : [],
    albumLinks: song.albumName && song.albumUrl ? [{ name: song.albumName, url: song.albumUrl }] : [],
    artworkUrl: song.thumbnailUrl,
    artworkExternalUrl: song.albumUrl ?? song.externalUrl,
    preserveArtworkAspectRatio: true,
    durationSeconds: song.durationSeconds,
    addedLabel: song.publishedAt,
    platformIds: ['spotify'],
    externalUrl: song.externalUrl,
  };
}

function SpotifyPlaylistDetail({
  playlist,
  songs,
  playlists,
  isLoadingSongs,
}: {
  playlist: PlatformPlaylist;
  songs: PlatformSong[];
  playlists: PlatformPlaylist[];
  isLoadingSongs: boolean;
}) {
  const tracks = songs.map(mapSong);
  const queue = useMusicQueue(tracks, playlist.id);
  const displayTracks = tracks.map((track) => ({ ...track, isPlaying: track.id === queue.activeTrackId }));
  const suggestions: MusicCollectionSuggestion[] = playlists
    .filter((candidate) => candidate.id !== playlist.id)
    .slice(0, 4)
    .map((candidate) => ({
      id: candidate.id,
      title: candidate.title,
      detail: `${candidate.itemCount.toLocaleString()} tracks`,
      artworkUrl: candidate.thumbnailUrl,
      route: { type: 'platformPlaylist', platformId: 'spotify' },
    }));

  return (
    <MusicCollectionDetailPage
      eyebrow="Spotify playlist"
      title={playlist.title}
      description={playlist.description}
      artworkUrl={playlist.thumbnailUrl}
      artworkExternalUrl={playlist.externalUrl}
      preserveArtworkAspectRatio
      backTo="/music/platforms/$platformId"
      backParams={{ platformId: 'spotify' }}
      backLabel="Back to Spotify playlists"
      ownerLabel={playlist.ownerName ?? 'Spotify'}
      updatedAt={playlist.publishedAt}
      songsLabel={`${playlist.itemCount.toLocaleString()} tracks`}
      chips={['Spotify', 'Platform playlist']}
      tracks={displayTracks}
      isLoadingTracks={isLoadingSongs}
      emptyTrackLabel="This Spotify playlist has no available tracks"
      activeTrack={queue.activeTrack}
      queuedTracks={queue.queuedTracks}
      suggestions={suggestions}
      actionSlot={playlist.externalUrl ? (
        <a
          href={playlist.externalUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex h-11 items-center gap-2 rounded-full bg-white px-4 text-sm font-black text-slate-900 transition hover:bg-violet-50 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
        >
          <MusicPlatformIcon platformId="spotify" className="h-8 w-8 text-[#1ed760]" />
          Open on Spotify
        </a>
      ) : <SpotifyAttribution />}
      onPlayAll={queue.playAll}
      onShuffle={queue.shuffle}
      onPlayTrack={queue.playTrack}
      onQueueTrack={queue.queueTrack}
      onClearQueue={queue.clearQueue}
    />
  );
}

function SpotifyPlaylistGrid({
  playlists,
  onSelect,
}: {
  playlists: PlatformPlaylist[];
  onSelect: (playlist: PlatformPlaylist) => void;
}) {
  if (playlists.length === 0) {
    return (
      <section className="rounded-2xl bg-white/70 p-6">
        <h2 className="text-xl font-black text-slate-950">No playlists available</h2>
        <p className="mt-2 text-sm font-semibold text-slate-600">Spotify did not return any playlists for this account.</p>
      </section>
    );
  }

  return (
    <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
      {playlists.map((playlist) => (
        <SpotifyPlaylistCard key={playlist.id} playlist={playlist} onSelect={onSelect} />
      ))}
    </section>
  );
}

function SpotifyPageBody({
  browser,
  isConnected,
  onSelectPlaylist,
}: {
  browser: ReturnType<typeof useSpotifyBrowser>;
  isConnected: boolean;
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}) {
  if (browser.isLoading) {
    return <section className="rounded-2xl bg-white/70 p-6 text-sm font-semibold text-slate-600">Loading Spotify connection…</section>;
  }

  if (browser.selectedPlaylist) {
    return (
      <SpotifyPlaylistDetail
        playlist={browser.selectedPlaylist}
        songs={browser.songs}
        playlists={browser.playlists}
        isLoadingSongs={browser.isLoadingSongs}
      />
    );
  }

  if (isConnected && !browser.needsReconnect) {
    return <SpotifyPlaylistGrid playlists={browser.playlists} onSelect={onSelectPlaylist} />;
  }

  return null;
}

function requestedSpotifyPlaylist({
  isConnected,
  isLoading,
  playlistId,
  playlists,
}: {
  isConnected: boolean;
  isLoading: boolean;
  playlistId: string | null;
  playlists: PlatformPlaylist[];
}) {
  if (!isConnected || isLoading || !playlistId) return undefined;
  return playlists.find((playlist) => playlist.id === playlistId);
}

export function SpotifyMusicPlatformPage({ playlistId = null }: { playlistId?: string | null }) {
  const navigate = useNavigate();
  const browser = useSpotifyBrowser();
  const isConnected = Boolean(browser.status?.isConnected);
  const {
    clearSelection,
    isLoading,
    playlists,
    selectPlaylist: loadPlaylist,
    selectedPlaylist,
  } = browser;

  useEffect(() => {
    if (!playlistId && selectedPlaylist) clearSelection();
  }, [clearSelection, playlistId, selectedPlaylist]);

  useEffect(() => {
    const routePlaylist = requestedSpotifyPlaylist({ isConnected, isLoading, playlistId, playlists });
    if (routePlaylist && selectedPlaylist?.id !== routePlaylist.id) {
      void loadPlaylist(routePlaylist);
    }
  }, [isConnected, isLoading, loadPlaylist, playlistId, playlists, selectedPlaylist]);

  const selectPlaylist = (playlist: PlatformPlaylist) => {
    void navigate({
      to: '/music/platforms/$platformId/playlists/$playlistId',
      params: { platformId: 'spotify', playlistId: playlist.id },
    });
    void browser.selectPlaylist(playlist);
  };

  return (
    <MusicPageShell>
      <div className="space-y-6">
        {!browser.selectedPlaylist ? (
          <SpotifyHeader
            accountName={browser.status?.displayName}
            isConnected={isConnected}
            needsReconnect={browser.needsReconnect}
            playlistCount={browser.playlists.length}
            onConnect={() => void browser.connect()}
            onDisconnect={() => void browser.disconnect()}
            onRefresh={() => void browser.refresh()}
          />
        ) : null}

        {browser.error ? (
          <section className="rounded-2xl bg-rose-50 p-4 text-sm font-semibold text-rose-800" role="alert">
            {browser.error}
          </section>
        ) : null}

        <SpotifyPageBody browser={browser} isConnected={isConnected} onSelectPlaylist={selectPlaylist} />
      </div>
    </MusicPageShell>
  );
}
