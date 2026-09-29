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
import { MusicPlatformHeader, MusicPlatformPlaylistGrid } from './MusicPlatformBrowser';
import { MusicPlatformDisconnectReview } from './MusicPlatformDisconnectReview';

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

function useSpotifyConnection(loadPlaylists: (forceRefresh?: boolean) => Promise<void>) {
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
  return { status, isLoading, error, needsReconnect, connect };
}

function useSpotifyBrowser() {
  const playlists = useSpotifyPlaylistBrowser();
  const connection = useSpotifyConnection(playlists.loadPlaylists);
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

  if (isConnected && !browser.needsReconnect && !browser.error) {
    return <MusicPlatformPlaylistGrid platformId="spotify" playlists={browser.playlists} onSelect={onSelectPlaylist} />;
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
  const [disconnectOpen, setDisconnectOpen] = useState(false);
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
        {(!browser.selectedPlaylist || browser.needsReconnect) ? (
          <MusicPlatformHeader
            platformId="spotify"
            isLoading={isLoading}
            accountName={browser.status?.displayName}
            isConnected={isConnected}
            needsReconnect={browser.needsReconnect}
            onConnect={() => void browser.connect()}
            onDisconnect={() => setDisconnectOpen(true)}
            onRefresh={() => void browser.refresh()}
          />
        ) : null}

        <MusicPlatformDisconnectReview service="spotify" open={disconnectOpen} onClose={() => setDisconnectOpen(false)} onDisconnected={() => void navigate({ to: '/music/platforms' })} />

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
