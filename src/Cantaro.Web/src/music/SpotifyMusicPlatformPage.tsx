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
      className={`inline-flex items-center gap-2 border border-border-strong bg-surface font-black text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${
        compact ? 'px-3 py-1.5 text-xs' : 'px-4 py-2 text-sm'
      }`}
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
    <section className="border-y border-border-subtle bg-surface-subtle p-6">
      <div className="flex flex-wrap items-end justify-between gap-5">
        <div className="max-w-2xl">
          <div className="flex items-center gap-3">
            <MusicPlatformIcon platformId="spotify" className="h-12 w-12 shrink-0 text-[#1ed760]" title="Spotify" />
            <h1 className="text-4xl leading-tight font-black text-content sm:text-5xl">Spotify</h1>
          </div>
          <p className="mt-3 text-sm leading-6 font-semibold text-content-muted">{description}</p>
          <p className="mt-2 text-xs font-semibold text-content-muted">Spotify content is shown with links back to Spotify.</p>
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
        <button type="button" className="min-h-11 border border-border-strong bg-surface px-5 text-sm font-black text-content transition hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus" onClick={onRefresh}>
          Refresh
        </button>
      ) : (
        <button type="button" className="min-h-11 bg-personal-accent px-5 text-sm font-black text-personal-accent-contrast transition hover:bg-personal-accent-hover focus-visible:outline-2 focus-visible:outline-focus" onClick={onConnect}>
          {needsReconnect ? 'Reconnect Spotify' : 'Connect Spotify'}
        </button>
      )}
      {isConnected ? (
        <button type="button" className="min-h-11 border border-danger-border bg-surface px-5 text-sm font-black text-danger-content transition hover:bg-danger-surface focus-visible:outline-2 focus-visible:outline-focus" onClick={onDisconnect}>
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
    <article className="overflow-hidden bg-surface-subtle">
      <button
        type="button"
        onClick={() => onSelect(playlist)}
        className="group block w-full text-left focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none focus-visible:ring-inset"
      >
        {playlist.thumbnailUrl ? (
          <img src={playlist.thumbnailUrl} alt="" className="aspect-square w-full bg-surface object-contain" />
        ) : (
          <span className="flex aspect-square w-full items-center justify-center bg-surface-subtle text-content-muted">
            <MusicPlatformIcon platformId="spotify" className="h-10 w-10" />
          </span>
        )}
        <span className="block p-4">
          <span className="block truncate font-black text-content">{playlist.title}</span>
          <span className="mt-1 block text-xs font-semibold text-content-muted">
            {playlist.itemCount.toLocaleString()} tracks{playlist.ownerName ? ` · ${playlist.ownerName}` : ''}
          </span>
        </span>
      </button>
      <div className="flex items-center justify-between gap-3 border-t border-border-subtle px-4 py-3">
        <SpotifyAttribution compact />
        {playlist.externalUrl ? (
          <a
            href={playlist.externalUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex min-h-9 items-center bg-personal-accent px-3 text-xs font-black text-personal-accent-contrast transition-colors hover:bg-personal-accent-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
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
      tracks={tracks}
      isLoadingTracks={isLoadingSongs}
      emptyTrackLabel="This Spotify playlist has no available tracks"
      suggestions={suggestions}
      actionSlot={playlist.externalUrl ? (
        <a
          href={playlist.externalUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex h-11 items-center gap-2 border border-border-strong bg-surface px-4 text-sm font-black text-content transition hover:bg-surface-hover hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
        >
          <MusicPlatformIcon platformId="spotify" className="h-8 w-8 text-[#1ed760]" />
          Open on Spotify
        </a>
      ) : <SpotifyAttribution />}
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
      <section className="border-y border-border-subtle py-6">
        <h2 className="text-xl font-black text-content">No playlists available</h2>
        <p className="mt-2 text-sm font-semibold text-content-muted">Spotify did not return any playlists for this account.</p>
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
    return <section className="border-y border-border-subtle py-6 text-sm font-semibold text-content-muted">Loading Spotify connection…</section>;
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
          <section className="border-y border-danger-border bg-danger-surface p-4 text-sm font-semibold text-danger-content" role="alert">
            {browser.error}
          </section>
        ) : null}

        <SpotifyPageBody browser={browser} isConnected={isConnected} onSelectPlaylist={selectPlaylist} />
      </div>
    </MusicPageShell>
  );
}
