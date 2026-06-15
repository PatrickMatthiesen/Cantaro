import { useEffect, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import {
  platformCatalog,
  usePlatformPlaylistsState,
  type PlatformId,
  type PlatformPlaylist,
  type PlatformSong,
} from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicPageShell } from './MusicPageShell';

type PlaylistRouteSyncAction =
  | { type: 'clear' }
  | { type: 'select'; playlist: PlatformPlaylist }
  | { type: 'idle' };

interface PlatformPageConfig {
  id: PlatformId;
  name: string;
  fallbackArtist: string;
  heroClassName: string;
  heroOverlayClassName: string;
  accentTextClassName: string;
  emptyArtworkClassName: string;
  missingClassName: string;
  missingButtonClassName: string;
}

function getPlatformPageConfig(platformId: PlatformId): PlatformPageConfig {
  const catalogEntry = platformCatalog.find((platform) => platform.id === platformId);
  const name = catalogEntry?.name ?? platformId;

  if (platformId === 'spotify') {
    return {
      id: platformId,
      name,
      fallbackArtist: 'Spotify',
      heroClassName: 'bg-[#1db954] shadow-[0_28px_90px_rgba(22,163,74,0.18)]',
      heroOverlayClassName: 'bg-linear-to-r from-[#063d1b]/92 via-[#15803d]/78 to-[#86efac]/35',
      accentTextClassName: 'text-emerald-600',
      emptyArtworkClassName: 'bg-emerald-50 text-emerald-700',
      missingClassName: 'bg-emerald-50 text-emerald-900 shadow-[0_24px_80px_rgba(22,163,74,0.08)]',
      missingButtonClassName: 'bg-emerald-950 text-white hover:bg-emerald-900',
    };
  }

  return {
    id: 'youtube',
    name,
    fallbackArtist: 'YouTube',
    heroClassName: 'bg-[#ef4444] shadow-[0_28px_90px_rgba(185,28,28,0.18)]',
    heroOverlayClassName: 'bg-linear-to-r from-[#3b0b16]/92 via-[#dc2626]/78 to-[#f9a8d4]/35',
    accentTextClassName: 'text-rose-600',
    emptyArtworkClassName: 'bg-rose-50 text-rose-600',
    missingClassName: 'bg-rose-50 text-rose-800 shadow-[0_24px_80px_rgba(185,28,28,0.08)]',
    missingButtonClassName: 'bg-rose-900 text-white hover:bg-rose-800',
  };
}

function canSyncPlaylistRoute(isConnected: boolean, isLoading: boolean) {
  return isConnected && !isLoading;
}

function getRoutePlaylist(playlistId: string | null, playlists: PlatformPlaylist[]) {
  return playlistId ? playlists.find((playlist) => playlist.id === playlistId) ?? null : null;
}

function shouldSelectRoutePlaylist(
  playlistId: string | null,
  routePlaylist: PlatformPlaylist | null,
  selectedPlaylist: PlatformPlaylist | null,
): routePlaylist is PlatformPlaylist {
  return Boolean(playlistId && routePlaylist && selectedPlaylist?.id !== playlistId);
}

function shouldClearRoutePlaylist(
  playlistId: string | null,
  routePlaylist: PlatformPlaylist | null,
  selectedPlaylist: PlatformPlaylist | null,
) {
  return Boolean(selectedPlaylist && (!playlistId || !routePlaylist));
}

function getPlaylistRouteSyncAction({
  isConnected,
  isLoading,
  playlistId,
  playlists,
  selectedPlaylist,
}: {
  isConnected: boolean;
  isLoading: boolean;
  playlistId: string | null;
  playlists: PlatformPlaylist[];
  selectedPlaylist: PlatformPlaylist | null;
}): PlaylistRouteSyncAction {
  if (!canSyncPlaylistRoute(isConnected, isLoading)) {
    return { type: 'idle' };
  }

  const routePlaylist = getRoutePlaylist(playlistId, playlists);
  if (shouldSelectRoutePlaylist(playlistId, routePlaylist, selectedPlaylist)) {
    return { type: 'select', playlist: routePlaylist };
  }

  return shouldClearRoutePlaylist(playlistId, routePlaylist, selectedPlaylist) ? { type: 'clear' } : { type: 'idle' };
}

function useSyncYouTubePlaylistRoute({
  clearSelectedPlaylist,
  isConnected,
  isLoading,
  playlistId,
  playlists,
  selectPlaylist,
  selectedPlaylist,
}: {
  clearSelectedPlaylist: () => void;
  isConnected: boolean;
  isLoading: boolean;
  playlistId: string | null;
  playlists: PlatformPlaylist[];
  selectPlaylist: (playlist: PlatformPlaylist) => Promise<void>;
  selectedPlaylist: PlatformPlaylist | null;
}) {
  useEffect(() => {
    const action = getPlaylistRouteSyncAction({
      isConnected,
      isLoading,
      playlistId,
      playlists,
      selectedPlaylist,
    });

    if (action.type === 'clear') {
      clearSelectedPlaylist();
    }

    if (action.type === 'select') {
      void selectPlaylist(action.playlist);
    }
  }, [clearSelectedPlaylist, isConnected, isLoading, playlistId, playlists, selectPlaylist, selectedPlaylist]);
}

function isMissingPlaylistRoute({
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
  return Boolean(
    canSyncPlaylistRoute(isConnected, isLoading)
    && playlistId
    && !getRoutePlaylist(playlistId, playlists),
  );
}

function YouTubePageHeader({
  accountName,
  config,
  isConnected,
  playlistCount,
  onRefresh,
  onDisconnect,
}: {
  accountName?: string | null;
  config: PlatformPageConfig;
  isConnected: boolean;
  playlistCount: number;
  onRefresh: () => void;
  onDisconnect: () => void;
}) {
  return (
    <section className={`relative overflow-hidden rounded-3xl p-6 text-white ${config.heroClassName}`}>
      <div className={`absolute inset-0 ${config.heroOverlayClassName}`} />
      <div className="relative flex flex-wrap items-end justify-between gap-5">
        <div>
          <p className="text-xs font-black tracking-[0.22em] text-white/75 uppercase">Music platform</p>
          <h1 className="mt-2 text-4xl leading-tight font-black sm:text-5xl">{config.name}</h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 font-semibold text-white/82">
            {isConnected
              ? `Connected as ${accountName ?? `${config.name} account`} with ${playlistCount.toLocaleString()} playlists ready to browse.`
              : `Connect ${config.name} to bring playlists into your Cantaro music page.`}
          </p>
        </div>
        {isConnected ? (
          <div className="flex flex-wrap gap-2">
            <button type="button" className="rounded-2xl bg-white/18 px-5 py-3 text-sm font-black backdrop-blur transition hover:bg-white/25" onClick={onRefresh}>
              Refresh
            </button>
            <button type="button" className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white transition hover:bg-slate-800" onClick={onDisconnect}>
              Disconnect
            </button>
          </div>
        ) : null}
      </div>
    </section>
  );
}

function YouTubeDisconnectedPanel({ config, onConnect }: { config: PlatformPageConfig; onConnect: () => void }) {
  return (
    <section className="rounded-3xl bg-white/70 p-6 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <p className={`text-xs font-black tracking-[0.22em] uppercase ${config.accentTextClassName}`}>Not connected</p>
      <h2 className="mt-2 text-2xl font-black text-slate-950">Connect {config.name} to begin</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 font-medium text-slate-500">
        Once connected, your {config.name} playlists show up here as part of the same music browsing surface as the rest of your library.
      </p>
      <button
        type="button"
        className="mt-5 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white transition hover:bg-slate-800"
        onClick={onConnect}
      >
        Connect {config.name}
      </button>
    </section>
  );
}

function YouTubePlaylistCard({
  config,
  playlist,
  onSelect,
}: {
  config: PlatformPageConfig;
  playlist: PlatformPlaylist;
  onSelect: (playlist: PlatformPlaylist) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onSelect(playlist)}
      className="group overflow-hidden rounded-3xl bg-white/70 text-left shadow-[0_24px_80px_rgba(88,74,150,0.08)] transition hover:-translate-y-0.5 hover:bg-white"
    >
      {playlist.thumbnailUrl ? (
        <img src={playlist.thumbnailUrl} alt="" className="h-44 w-full object-cover transition group-hover:scale-105" />
      ) : (
        <div className={`flex h-44 w-full items-center justify-center text-sm font-black ${config.emptyArtworkClassName}`}>{config.name}</div>
      )}
      <div className="p-4">
        <h3 className="line-clamp-2 text-base font-black text-slate-950">{playlist.title}</h3>
        {playlist.description ? <p className="mt-2 line-clamp-2 text-sm font-medium text-slate-500">{playlist.description}</p> : null}
      </div>
    </button>
  );
}

function YouTubePlaylistGrid({
  config,
  playlists,
  onSelectPlaylist,
}: {
  config: PlatformPageConfig;
  playlists: PlatformPlaylist[];
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}) {
  if (playlists.length === 0) {
    return (
      <section className="rounded-3xl border border-dashed border-[#ded8f2] bg-white/54 p-6">
        <p className="font-black text-slate-950">No playlists found</p>
        <p className="mt-1 text-sm font-medium text-slate-500">This account has no {config.name} playlists available to Cantaro.</p>
      </section>
    );
  }

  return (
    <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
      {playlists.map((playlist) => (
        <YouTubePlaylistCard key={playlist.id} config={config} playlist={playlist} onSelect={onSelectPlaylist} />
      ))}
    </section>
  );
}

function mapPlatformSongToTrack(item: PlatformSong, config: PlatformPageConfig, activeTrackId?: string): MusicCollectionTrack {
  return {
    id: item.id,
    title: item.title,
    artist: item.artistName ?? config.fallbackArtist,
    album: config.name,
    artworkUrl: item.thumbnailUrl,
    addedLabel: item.publishedAt,
    platformNames: [config.name],
    isPlaying: item.id === activeTrackId,
  };
}

function getPlatformSuggestions(
  playlists: PlatformPlaylist[],
  selectedPlaylistId: string,
  platformId: PlatformId,
): MusicCollectionSuggestion[] {
  return playlists
    .filter((playlist) => playlist.id !== selectedPlaylistId)
    .slice(0, 5)
    .map((playlist) => ({
      id: playlist.id,
      title: playlist.title,
      detail: `${playlist.itemCount.toLocaleString()} songs`,
      artworkUrl: playlist.thumbnailUrl,
      route: { type: 'platformPlaylist', platformId },
    }));
}

function YouTubePlaylistDetail({
  config,
  playlist,
  playlistItems,
  isLoadingItems,
  playlists,
}: {
  config: PlatformPageConfig;
  playlist: PlatformPlaylist;
  playlistItems: PlatformSong[];
  isLoadingItems: boolean;
  playlists: PlatformPlaylist[];
}) {
  const [activeTrackId, setActiveTrackId] = useState<string | undefined>();
  const [queuedTrackId, setQueuedTrackId] = useState<string | undefined>();

  useEffect(() => {
    setActiveTrackId(undefined);
    setQueuedTrackId(undefined);
  }, [playlist.id]);

  const tracks = playlistItems.map((item) => mapPlatformSongToTrack(item, config, activeTrackId));
  const activeTrack = tracks.find((track) => track.id === activeTrackId);
  const queuedTrack = tracks.find((track) => track.id === queuedTrackId);
  const trackIds = tracks.map((track) => track.id);

  const playTrack = (track: MusicCollectionTrack) => {
    setActiveTrackId(track.id);
  };

  const queueTrack = (track: MusicCollectionTrack) => {
    setQueuedTrackId(track.id);
  };

  const clearQueue = () => {
    setActiveTrackId(undefined);
    setQueuedTrackId(undefined);
  };

  const playFirstTrack = () => {
    setActiveTrackId(trackIds[0]);
  };

  const playRandomTrack = () => {
    if (trackIds.length === 0) return;
    setActiveTrackId(trackIds[Math.floor(Math.random() * trackIds.length)]);
  };

  return (
    <MusicCollectionDetailPage
      eyebrow="Playlist"
      title={playlist.title}
      description={playlist.description}
      artworkUrl={playlist.thumbnailUrl}
      backTo="/music/platforms/$platformId"
      backParams={{ platformId: config.id }}
      backLabel={`Back to ${config.name} playlists`}
      ownerLabel={config.name}
      updatedAt={playlist.publishedAt}
      songsLabel={`${playlist.itemCount.toLocaleString()} songs`}
      chips={[config.name, 'Platform playlist']}
      tracks={tracks}
      isLoadingTracks={isLoadingItems}
      emptyTrackLabel="This playlist has no items yet"
      activeTrack={activeTrack}
      queuedTrack={queuedTrack}
      suggestions={getPlatformSuggestions(playlists, playlist.id, config.id)}
      onPlayAll={playFirstTrack}
      onShuffle={playRandomTrack}
      onPlayTrack={playTrack}
      onQueueTrack={queueTrack}
      onClearQueue={clearQueue}
    />
  );
}

function YouTubePlaylistMissingState({
  config,
  playlistId,
  onBack,
  onRefresh,
}: {
  config: PlatformPageConfig;
  playlistId: string;
  onBack: () => void;
  onRefresh: () => void;
}) {
  return (
    <section className={`rounded-3xl p-6 ${config.missingClassName}`}>
      <p className="text-xs font-black tracking-[0.2em] uppercase">Playlist not found</p>
      <h2 className="mt-2 text-2xl font-black">This {config.name} playlist is not available</h2>
      <p className="mt-2 text-sm font-semibold">
        Cantaro could not find playlist <span className="font-black">{playlistId}</span> in the connected account.
      </p>
      <div className="mt-5 flex flex-wrap gap-2">
        <button type="button" className={`rounded-2xl px-5 py-3 text-sm font-black transition ${config.missingButtonClassName}`} onClick={onBack}>
          All playlists
        </button>
        <button type="button" className="rounded-2xl bg-white/80 px-5 py-3 text-sm font-black transition hover:bg-white" onClick={onRefresh}>
          Refresh playlists
        </button>
      </div>
    </section>
  );
}

function YouTubeError({ error }: { error: string | null }) {
  if (!error) return null;

  return (
    <section className="rounded-3xl border border-rose-200 bg-rose-50 p-4 text-sm font-semibold text-rose-700">
      {error}
    </section>
  );
}

function YouTubeLoadingState({ config }: { config: PlatformPageConfig }) {
  return (
    <section className="rounded-3xl bg-white/70 p-6 text-sm font-semibold text-slate-500 shadow-[0_24px_80px_rgba(88,74,150,0.08)]">
      Loading {config.name} playlists...
    </section>
  );
}

function getMissingPlaylistId({
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
  return isMissingPlaylistRoute({ isConnected, isLoading, playlistId, playlists }) ? playlistId : null;
}

function YouTubePlatformContent({
  config,
  isConnected,
  isLoading,
  isLoadingItems,
  missingPlaylistId,
  playlistItems,
  playlists,
  selectedPlaylist,
  onBackToPlaylists,
  onConnect,
  onRefresh,
  onSelectPlaylist,
}: {
  config: PlatformPageConfig;
  isConnected: boolean;
  isLoading: boolean;
  isLoadingItems: boolean;
  missingPlaylistId: string | null;
  playlistItems: PlatformSong[];
  playlists: PlatformPlaylist[];
  selectedPlaylist: PlatformPlaylist | null;
  onBackToPlaylists: () => void;
  onConnect: () => void;
  onRefresh: () => void;
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}) {
  if (isLoading) return <YouTubeLoadingState config={config} />;
  if (!isConnected) return <YouTubeDisconnectedPanel config={config} onConnect={onConnect} />;

  if (missingPlaylistId) {
    return (
      <YouTubePlaylistMissingState
        config={config}
        playlistId={missingPlaylistId}
        onBack={onBackToPlaylists}
        onRefresh={onRefresh}
      />
    );
  }

  if (selectedPlaylist) {
    return (
      <YouTubePlaylistDetail
        config={config}
        playlist={selectedPlaylist}
        playlistItems={playlistItems}
        isLoadingItems={isLoadingItems}
        playlists={playlists}
      />
    );
  }

  return <YouTubePlaylistGrid config={config} playlists={playlists} onSelectPlaylist={onSelectPlaylist} />;
}

export function YouTubeMusicPlatformPage({
  platformId = 'youtube',
  playlistId = null,
}: {
  platformId?: PlatformId;
  playlistId?: string | null;
}) {
  const navigate = useNavigate();
  const config = getPlatformPageConfig(platformId);
  const {
    status,
    playlists,
    selectedPlaylist,
    playlistItems,
    isLoading,
    isLoadingItems,
    error,
    connect,
    disconnect,
    selectPlaylist,
    clearSelectedPlaylist,
    refreshPlaylists,
  } = usePlatformPlaylistsState(config.id);
  const isConnected = Boolean(status?.isConnected);

  useSyncYouTubePlaylistRoute({
    clearSelectedPlaylist,
    isConnected,
    isLoading,
    playlistId,
    playlists,
    selectPlaylist,
    selectedPlaylist,
  });

  const handleSelectPlaylist = (playlist: PlatformPlaylist) => {
    void navigate({
      to: '/music/platforms/$platformId/playlists/$playlistId',
      params: { platformId: config.id, playlistId: playlist.id },
    });
    void selectPlaylist(playlist);
  };

  const handleBackToPlaylists = () => {
    void navigate({ to: '/music/platforms/$platformId', params: { platformId: config.id } });
    clearSelectedPlaylist();
  };

  const missingPlaylistId = getMissingPlaylistId({ isConnected, isLoading, playlistId, playlists });
  const isPlaylistDetail = Boolean(selectedPlaylist || missingPlaylistId);

  return (
    <MusicPageShell>
      <div className="space-y-6">
        {!isPlaylistDetail ? (
          <YouTubePageHeader
            accountName={status?.displayName}
            config={config}
            isConnected={isConnected}
            playlistCount={playlists.length}
            onRefresh={() => void refreshPlaylists()}
            onDisconnect={() => void disconnect()}
          />
        ) : null}

        <YouTubeError error={error} />

        <YouTubePlatformContent
          config={config}
          isConnected={isConnected}
          isLoading={isLoading}
          isLoadingItems={isLoadingItems}
          missingPlaylistId={missingPlaylistId}
          playlistItems={playlistItems}
          playlists={playlists}
          selectedPlaylist={selectedPlaylist}
          onBackToPlaylists={handleBackToPlaylists}
          onConnect={() => void connect()}
          onRefresh={() => void refreshPlaylists()}
          onSelectPlaylist={handleSelectPlaylist}
        />
      </div>
    </MusicPageShell>
  );
}
