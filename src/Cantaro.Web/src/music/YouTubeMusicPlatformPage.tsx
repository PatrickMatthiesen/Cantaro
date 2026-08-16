import { useEffect } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useYouTubePlaylistsState, type PlatformPlaylist, type PlatformSong } from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
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
    <section className="relative overflow-hidden border-y border-[#ef4444]/45 bg-[#ef4444] p-6 text-content-inverse">
      <div className="absolute inset-0 bg-linear-to-r from-[#3b0b16]/92 via-[#dc2626]/78 to-[#f9a8d4]/35" />
      <div className="relative flex flex-wrap items-end justify-between gap-5">
        <div>
          <p className="text-xs font-black tracking-[0.22em] text-content-inverse/75 uppercase">Music platform</p>
          <h1 className="mt-2 text-4xl leading-tight font-black sm:text-5xl">YouTube</h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 font-semibold text-content-inverse/82">{description}</p>
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
        <button type="button" className="min-h-11 bg-surface px-5 text-sm font-black text-content transition hover:bg-rose-50" onClick={onConnect}>
          Reconnect
        </button>
      ) : (
        <button type="button" className="min-h-11 border border-white/30 bg-surface/18 px-5 text-sm font-black transition hover:bg-surface/25" onClick={onRefresh}>
          Refresh
        </button>
      )}
      <button type="button" className="min-h-11 border border-white/30 px-5 text-sm font-black text-white transition hover:bg-white/15" onClick={onDisconnect}>
        Disconnect
      </button>
    </div>
  );
}

function YouTubeReconnectPanel({ accountName, onConnect }: { accountName?: string | null; onConnect: () => void }) {
  return (
    <section className="border-y border-warning-border bg-warning-surface p-6 text-warning-content">
      <p className="text-xs font-black tracking-[0.18em] uppercase">Connection expired</p>
      <h2 className="mt-2 text-2xl font-black text-content">Reconnect YouTube</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 font-semibold text-amber-900">
        Google no longer accepts the saved token for {accountName ?? 'this YouTube account'}. Reconnect once and Cantaro will store a fresh permission grant.
      </p>
      <button
        type="button"
        className="mt-5 min-h-11 bg-personal-accent px-5 text-sm font-black text-personal-accent-contrast transition hover:bg-personal-accent-hover"
        onClick={onConnect}
      >
        Reconnect YouTube
      </button>
    </section>
  );
}

function YouTubeDisconnectedPanel({ onConnect }: { onConnect: () => void }) {
  return (
    <section className="border-y border-border-subtle py-6">
      <p className="text-xs font-black tracking-[0.22em] text-rose-600 uppercase">Not connected</p>
      <h2 className="mt-2 text-2xl font-black text-content">Connect YouTube to begin</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 font-medium text-content-muted">
        Once connected, your YouTube playlists show up here as part of the same music browsing surface as the rest of your library.
      </p>
      <button
        type="button"
        className="mt-5 min-h-11 bg-personal-accent px-5 text-sm font-black text-personal-accent-contrast transition hover:bg-personal-accent-hover"
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
      className="group overflow-hidden bg-surface-subtle text-left transition hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus"
    >
      {playlist.thumbnailUrl ? (
        <img src={playlist.thumbnailUrl} alt="" className="h-44 w-full object-cover transition group-hover:scale-105" />
      ) : (
        <div className="flex h-44 w-full items-center justify-center bg-rose-50 text-sm font-black text-rose-600">YouTube</div>
      )}
      <div className="p-4">
        <h3 className="line-clamp-2 text-base font-black text-content">{playlist.title}</h3>
        {playlist.description ? <p className="mt-2 line-clamp-2 text-sm font-medium text-content-muted">{playlist.description}</p> : null}
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
      <section className="border-y border-border-subtle py-6">
        <p className="font-black text-content">No playlists found</p>
        <p className="mt-1 text-sm font-medium text-content-muted">This account has no YouTube playlists available to Cantaro.</p>
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
  const tracks = playlistItems.map(mapPlatformSongToTrack);

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
      suggestions={getPlatformSuggestions(playlists, playlist.id)}
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
    <section className="border-y border-danger-border bg-danger-surface p-6 text-danger-content">
      <p className="text-xs font-black tracking-[0.2em] uppercase">Playlist not found</p>
      <h2 className="mt-2 text-2xl font-black">This YouTube playlist is not available</h2>
      <p className="mt-2 text-sm font-semibold">
        Cantaro could not find playlist <span className="font-black">{playlistId}</span> in the connected account.
      </p>
      <div className="mt-5 flex flex-wrap gap-2">
        <button type="button" className="min-h-11 bg-danger-action px-5 text-sm font-black text-danger-action-content transition hover:bg-danger-action-hover" onClick={onBack}>
          All playlists
        </button>
        <button type="button" className="min-h-11 border border-danger-border bg-surface px-5 text-sm font-black text-danger-content transition hover:bg-surface-hover" onClick={onRefresh}>
          Refresh playlists
        </button>
      </div>
    </section>
  );
}

function YouTubeError({ error }: { error: string | null }) {
  if (!error) return null;

  return (
    <section className="border-y border-danger-border bg-danger-surface p-4 text-sm font-semibold text-danger-content">
      {error}
    </section>
  );
}

function YouTubeLoadingState() {
  return (
    <section className="border-y border-border-subtle py-6 text-sm font-semibold text-content-muted">
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
