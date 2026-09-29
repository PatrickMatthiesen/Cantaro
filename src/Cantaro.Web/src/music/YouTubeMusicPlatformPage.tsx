import { useEffect, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useYouTubePlaylistsState, type PlatformPlaylist, type PlatformSong } from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicPageShell } from './MusicPageShell';
import { MusicPlatformHeader, MusicPlatformPlaylistGrid } from './MusicPlatformBrowser';
import { MusicPlatformDisconnectReview } from './MusicPlatformDisconnectReview';

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
    <section role="alert" className="border-y border-danger-border bg-danger-surface p-4 text-sm font-semibold text-danger-content">
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
  error,
  isConnected,
  isLoading,
  isLoadingItems,
  missingPlaylistId,
  needsReconnect,
  playlistItems,
  playlists,
  selectedPlaylist,
  onBackToPlaylists,
  onRefresh,
  onSelectPlaylist,
}: {
  error: string | null;
  isConnected: boolean;
  isLoading: boolean;
  isLoadingItems: boolean;
  missingPlaylistId: string | null;
  needsReconnect: boolean;
  playlistItems: PlatformSong[];
  playlists: PlatformPlaylist[];
  selectedPlaylist: PlatformPlaylist | null;
  onBackToPlaylists: () => void;
  onRefresh: () => void;
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}) {
  if (isLoading) return <YouTubeLoadingState />;
  if (needsReconnect || !isConnected || error) return null;

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

  return <MusicPlatformPlaylistGrid platformId="youtube" playlists={playlists} onSelect={onSelectPlaylist} />;
}

function hasPlaylistDetail(selectedPlaylist: PlatformPlaylist | null, missingPlaylistId: string | null, needsReconnect: boolean, error: string | null) {
  return !needsReconnect && !error && Boolean(selectedPlaylist || missingPlaylistId);
}

export function YouTubeMusicPlatformPage({ playlistId = null }: { playlistId?: string | null }) {
  const navigate = useNavigate();
  const [disconnectOpen, setDisconnectOpen] = useState(false);
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
  const isPlaylistDetail = hasPlaylistDetail(selectedPlaylist, missingPlaylistId, needsReconnect, error);

  return (
    <MusicPageShell>
      <div className="space-y-6">
        {!isPlaylistDetail ? (
          <MusicPlatformHeader
            platformId="youtube"
            isLoading={isLoading}
            accountName={status?.displayName}
            isConnected={isConnected}
            needsReconnect={needsReconnect}
            onConnect={() => void connect()}
            onRefresh={() => void refreshPlaylists()}
            onDisconnect={() => setDisconnectOpen(true)}
          />
        ) : null}

        <MusicPlatformDisconnectReview service="youtube" open={disconnectOpen} onClose={() => setDisconnectOpen(false)} onDisconnected={() => void navigate({ to: '/music/platforms' })} />

        <YouTubeError error={needsReconnect ? null : error} />

        <YouTubePlatformContent
          error={error}
          isConnected={isConnected}
          isLoading={isLoading}
          isLoadingItems={isLoadingItems}
          missingPlaylistId={missingPlaylistId}
          needsReconnect={needsReconnect}
          playlistItems={playlistItems}
          playlists={playlists}
          selectedPlaylist={selectedPlaylist}
          onBackToPlaylists={handleBackToPlaylists}
          onRefresh={() => void refreshPlaylists()}
          onSelectPlaylist={handleSelectPlaylist}
        />
      </div>
    </MusicPageShell>
  );
}
