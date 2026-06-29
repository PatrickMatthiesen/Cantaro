import { useEffect } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useYouTubePlaylistsState, type PlatformPlaylist, type PlatformSong } from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { useMusicQueue } from './useMusicQueue';
import { MusicPageShell } from './MusicPageShell';

type PlaylistRouteSyncAction =
  | { type: 'clear' }
  | { type: 'select'; playlist: PlatformPlaylist }
  | { type: 'idle' };

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
  isConnected,
  needsReconnect,
  playlistCount,
  onConnect,
  onRefresh,
  onDisconnect,
}: {
  accountName?: string | null;
  isConnected: boolean;
  needsReconnect: boolean;
  playlistCount: number;
  onConnect: () => void;
  onRefresh: () => void;
  onDisconnect: () => void;
}) {
  const description = getYouTubeHeaderDescription({ accountName, isConnected, needsReconnect, playlistCount });

  return (
    <section className="relative overflow-hidden rounded-3xl bg-[#ef4444] p-6 text-white shadow-[0_28px_90px_rgba(185,28,28,0.18)]">
      <div className="absolute inset-0 bg-linear-to-r from-[#3b0b16]/92 via-[#dc2626]/78 to-[#f9a8d4]/35" />
      <div className="relative flex flex-wrap items-end justify-between gap-5">
        <div>
          <p className="text-xs font-black tracking-[0.22em] text-white/75 uppercase">Music platform</p>
          <h1 className="mt-2 text-4xl leading-tight font-black sm:text-5xl">YouTube</h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 font-semibold text-white/82">{description}</p>
        </div>
        <YouTubeHeaderActions
          isConnected={isConnected}
          needsReconnect={needsReconnect}
          onConnect={onConnect}
          onRefresh={onRefresh}
          onDisconnect={onDisconnect}
        />
      </div>
    </section>
  );
}

function getYouTubeHeaderDescription({
  accountName,
  isConnected,
  needsReconnect,
  playlistCount,
}: {
  accountName?: string | null;
  isConnected: boolean;
  needsReconnect: boolean;
  playlistCount: number;
}) {
  if (needsReconnect) {
    return `Connected as ${accountName ?? 'YouTube account'}, but Cantaro needs permission again before it can browse playlists.`;
  }

  if (isConnected) {
    return `Connected as ${accountName ?? 'YouTube account'} with ${playlistCount.toLocaleString()} playlists ready to browse.`;
  }

  return 'Connect YouTube to bring playlists into your Cantaro music page.';
}

function YouTubeHeaderActions({
  isConnected,
  needsReconnect,
  onConnect,
  onRefresh,
  onDisconnect,
}: {
  isConnected: boolean;
  needsReconnect: boolean;
  onConnect: () => void;
  onRefresh: () => void;
  onDisconnect: () => void;
}) {
  if (!isConnected) return null;

  return (
    <div className="flex flex-wrap gap-2">
      {needsReconnect ? (
        <button type="button" className="rounded-2xl bg-white px-5 py-3 text-sm font-black text-slate-950 transition hover:bg-rose-50" onClick={onConnect}>
          Reconnect
        </button>
      ) : (
        <button type="button" className="rounded-2xl bg-white/18 px-5 py-3 text-sm font-black backdrop-blur transition hover:bg-white/25" onClick={onRefresh}>
          Refresh
        </button>
      )}
      <button type="button" className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white transition hover:bg-slate-800" onClick={onDisconnect}>
        Disconnect
      </button>
    </div>
  );
}

function YouTubeReconnectPanel({ accountName, onConnect }: { accountName?: string | null; onConnect: () => void }) {
  return (
    <section className="rounded-3xl border border-amber-200 bg-amber-50/80 p-6 text-amber-950 shadow-[0_20px_60px_rgba(146,64,14,0.08)]">
      <p className="text-xs font-black tracking-[0.18em] uppercase">Connection expired</p>
      <h2 className="mt-2 text-2xl font-black text-slate-950">Reconnect YouTube</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 font-semibold text-amber-900">
        Google no longer accepts the saved token for {accountName ?? 'this YouTube account'}. Reconnect once and Cantaro will store a fresh permission grant.
      </p>
      <button
        type="button"
        className="mt-5 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white transition hover:bg-slate-800"
        onClick={onConnect}
      >
        Reconnect YouTube
      </button>
    </section>
  );
}

function YouTubeDisconnectedPanel({ onConnect }: { onConnect: () => void }) {
  return (
    <section className="rounded-3xl bg-white/70 p-6 shadow-[0_24px_80px_rgba(88,74,150,0.08)] backdrop-blur-xl">
      <p className="text-xs font-black tracking-[0.22em] text-rose-600 uppercase">Not connected</p>
      <h2 className="mt-2 text-2xl font-black text-slate-950">Connect YouTube to begin</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 font-medium text-slate-500">
        Once connected, your YouTube playlists show up here as part of the same music browsing surface as the rest of your library.
      </p>
      <button
        type="button"
        className="mt-5 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-black text-white transition hover:bg-slate-800"
        onClick={onConnect}
      >
        Connect YouTube
      </button>
    </section>
  );
}

function YouTubePlaylistCard({ playlist, onSelect }: { playlist: PlatformPlaylist; onSelect: (playlist: PlatformPlaylist) => void }) {
  return (
    <button
      type="button"
      onClick={() => onSelect(playlist)}
      className="group overflow-hidden rounded-3xl bg-white/70 text-left shadow-[0_24px_80px_rgba(88,74,150,0.08)] transition hover:-translate-y-0.5 hover:bg-white"
    >
      {playlist.thumbnailUrl ? (
        <img src={playlist.thumbnailUrl} alt="" className="h-44 w-full object-cover transition group-hover:scale-105" />
      ) : (
        <div className="flex h-44 w-full items-center justify-center bg-rose-50 text-sm font-black text-rose-600">YouTube</div>
      )}
      <div className="p-4">
        <h3 className="line-clamp-2 text-base font-black text-slate-950">{playlist.title}</h3>
        {playlist.description ? <p className="mt-2 line-clamp-2 text-sm font-medium text-slate-500">{playlist.description}</p> : null}
      </div>
    </button>
  );
}

function YouTubePlaylistGrid({
  playlists,
  onSelectPlaylist,
}: {
  playlists: PlatformPlaylist[];
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}) {
  if (playlists.length === 0) {
    return (
      <section className="rounded-3xl border border-dashed border-[#ded8f2] bg-white/54 p-6">
        <p className="font-black text-slate-950">No playlists found</p>
        <p className="mt-1 text-sm font-medium text-slate-500">This account has no YouTube playlists available to Cantaro.</p>
      </section>
    );
  }

  return (
    <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
      {playlists.map((playlist) => (
        <YouTubePlaylistCard key={playlist.id} playlist={playlist} onSelect={onSelectPlaylist} />
      ))}
    </section>
  );
}

function mapPlatformSongToTrack(item: PlatformSong): MusicCollectionTrack {
  return {
    id: item.id,
    title: item.title,
    artist: item.artistName ?? 'YouTube',
    artworkUrl: item.thumbnailUrl,
    addedLabel: item.publishedAt,
    platformIds: ['youtube'],
  };
}

function getPlatformSuggestions(playlists: PlatformPlaylist[], selectedPlaylistId: string): MusicCollectionSuggestion[] {
  return playlists
    .filter((playlist) => playlist.id !== selectedPlaylistId)
    .slice(0, 5)
    .map((playlist) => ({
      id: playlist.id,
      title: playlist.title,
      detail: `${playlist.itemCount.toLocaleString()} songs`,
      artworkUrl: playlist.thumbnailUrl,
      route: { type: 'platformPlaylist', platformId: 'youtube' },
    }));
}

function YouTubePlaylistDetail({
  playlist,
  playlistItems,
  isLoadingItems,
  playlists,
}: {
  playlist: PlatformPlaylist;
  playlistItems: PlatformSong[];
  isLoadingItems: boolean;
  playlists: PlatformPlaylist[];
}) {
  const baseTracks = playlistItems.map(mapPlatformSongToTrack);
  const queue = useMusicQueue(baseTracks, playlist.id);
  const tracks = baseTracks.map((track) => ({ ...track, isPlaying: track.id === queue.activeTrackId }));

  return (
    <MusicCollectionDetailPage
      eyebrow="Playlist"
      title={playlist.title}
      description={playlist.description}
      artworkUrl={playlist.thumbnailUrl}
      backTo="/music/platforms/$platformId"
      backParams={{ platformId: 'youtube' }}
      backLabel="Back to YouTube playlists"
      ownerLabel="YouTube"
      updatedAt={playlist.publishedAt}
      songsLabel={`${playlist.itemCount.toLocaleString()} songs`}
      chips={['YouTube', 'Platform playlist']}
      tracks={tracks}
      isLoadingTracks={isLoadingItems}
      emptyTrackLabel="This playlist has no items yet"
      activeTrack={queue.activeTrack}
      queuedTracks={queue.queuedTracks}
      suggestions={getPlatformSuggestions(playlists, playlist.id)}
      onPlayAll={queue.playAll}
      onShuffle={queue.shuffle}
      onPlayTrack={queue.playTrack}
      onQueueTrack={queue.queueTrack}
      onClearQueue={queue.clearQueue}
    />
  );
}

function YouTubePlaylistMissingState({
  playlistId,
  onBack,
  onRefresh,
}: {
  playlistId: string;
  onBack: () => void;
  onRefresh: () => void;
}) {
  return (
    <section className="rounded-3xl bg-rose-50 p-6 text-rose-800 shadow-[0_24px_80px_rgba(185,28,28,0.08)]">
      <p className="text-xs font-black tracking-[0.2em] uppercase">Playlist not found</p>
      <h2 className="mt-2 text-2xl font-black">This YouTube playlist is not available</h2>
      <p className="mt-2 text-sm font-semibold">
        Cantaro could not find playlist <span className="font-black">{playlistId}</span> in the connected account.
      </p>
      <div className="mt-5 flex flex-wrap gap-2">
        <button type="button" className="rounded-2xl bg-rose-900 px-5 py-3 text-sm font-black text-white transition hover:bg-rose-800" onClick={onBack}>
          All playlists
        </button>
        <button type="button" className="rounded-2xl bg-white/80 px-5 py-3 text-sm font-black text-rose-800 transition hover:bg-white" onClick={onRefresh}>
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

function YouTubeLoadingState() {
  return (
    <section className="rounded-3xl bg-white/70 p-6 text-sm font-semibold text-slate-500 shadow-[0_24px_80px_rgba(88,74,150,0.08)]">
      Loading YouTube playlists...
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
  accountName,
  isConnected,
  isLoading,
  isLoadingItems,
  missingPlaylistId,
  needsReconnect,
  playlistItems,
  playlists,
  selectedPlaylist,
  onBackToPlaylists,
  onConnect,
  onRefresh,
  onSelectPlaylist,
}: {
  accountName?: string | null;
  isConnected: boolean;
  isLoading: boolean;
  isLoadingItems: boolean;
  missingPlaylistId: string | null;
  needsReconnect: boolean;
  playlistItems: PlatformSong[];
  playlists: PlatformPlaylist[];
  selectedPlaylist: PlatformPlaylist | null;
  onBackToPlaylists: () => void;
  onConnect: () => void;
  onRefresh: () => void;
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}) {
  if (isLoading) return <YouTubeLoadingState />;
  if (needsReconnect) return <YouTubeReconnectPanel accountName={accountName} onConnect={onConnect} />;
  if (!isConnected) return <YouTubeDisconnectedPanel onConnect={onConnect} />;

  return (
    <YouTubePlaylistContent
      isLoadingItems={isLoadingItems}
      missingPlaylistId={missingPlaylistId}
      playlistItems={playlistItems}
      playlists={playlists}
      selectedPlaylist={selectedPlaylist}
      onBackToPlaylists={onBackToPlaylists}
      onRefresh={onRefresh}
      onSelectPlaylist={onSelectPlaylist}
    />
  );
}

function YouTubePlaylistContent({
  isLoadingItems,
  missingPlaylistId,
  playlistItems,
  playlists,
  selectedPlaylist,
  onBackToPlaylists,
  onRefresh,
  onSelectPlaylist,
}: {
  isLoadingItems: boolean;
  missingPlaylistId: string | null;
  playlistItems: PlatformSong[];
  playlists: PlatformPlaylist[];
  selectedPlaylist: PlatformPlaylist | null;
  onBackToPlaylists: () => void;
  onRefresh: () => void;
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}) {
  if (missingPlaylistId) {
    return (
      <YouTubePlaylistMissingState
        playlistId={missingPlaylistId}
        onBack={onBackToPlaylists}
        onRefresh={onRefresh}
      />
    );
  }

  if (selectedPlaylist) {
    return (
      <YouTubePlaylistDetail
        playlist={selectedPlaylist}
        playlistItems={playlistItems}
        isLoadingItems={isLoadingItems}
        playlists={playlists}
      />
    );
  }

  return <YouTubePlaylistGrid playlists={playlists} onSelectPlaylist={onSelectPlaylist} />;
}

export function YouTubeMusicPlatformPage({ playlistId = null }: { playlistId?: string | null }) {
  const navigate = useNavigate();
  const {
    status,
    playlists,
    selectedPlaylist,
    playlistItems,
    isLoading,
    isLoadingItems,
    error,
    needsReconnect,
    connect,
    disconnect,
    selectPlaylist,
    clearSelectedPlaylist,
    refreshPlaylists,
  } = useYouTubePlaylistsState();
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
      params: { platformId: 'youtube', playlistId: playlist.id },
    });
    void selectPlaylist(playlist);
  };

  const handleBackToPlaylists = () => {
    void navigate({ to: '/music/platforms/$platformId', params: { platformId: 'youtube' } });
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
            isConnected={isConnected}
            needsReconnect={needsReconnect}
            playlistCount={playlists.length}
            onConnect={() => void connect()}
            onRefresh={() => void refreshPlaylists()}
            onDisconnect={() => void disconnect()}
          />
        ) : null}

        <YouTubeError error={error} />

        <YouTubePlatformContent
          accountName={status?.displayName}
          isConnected={isConnected}
          isLoading={isLoading}
          isLoadingItems={isLoadingItems}
          missingPlaylistId={missingPlaylistId}
          needsReconnect={needsReconnect}
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
