import { useEffect } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { GlassCard, GradientPageShell, PageLoadingState } from '@cantaro/client-shared/ui';
import {
  YouTubeDisconnectedState,
  YouTubePlaylistBrowser,
  YouTubePlaylistDetailView,
} from '@cantaro/client-shared/music';
import { useYouTubePlaylistsState } from '@cantaro/client-shared/music';
import type { ReactNode } from 'react';
import type { PlatformAccountStatus, PlatformPlaylist, PlatformSong } from '@cantaro/client-shared/music';

interface YouTubePlaylistsPageProps {
  embedded?: boolean;
  playlistId?: string | null;
}

interface YouTubePlaylistsContentProps {
  status: PlatformAccountStatus | null;
  playlists: PlatformPlaylist[];
  selectedPlaylist: PlatformPlaylist | null;
  playlistItems: PlatformSong[];
  isLoadingItems: boolean;
  missingPlaylistId: string | null;
  onConnect: () => void;
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
  onBack: () => void;
  onRefresh: () => void;
  onDisconnect: () => void;
}

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
    <GlassCard className="p-6">
      <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">Playlist not found</p>
      <h2 className="mt-2 text-2xl font-semibold text-gray-900">This YouTube playlist is not available</h2>
      <p className="mt-2 text-sm text-gray-600">
        Cantaro could not find playlist <span className="font-medium text-gray-800">{playlistId}</span> in the connected account.
      </p>
      <div className="mt-5 flex flex-wrap gap-2">
        <button
          type="button"
          className="rounded-2xl bg-gray-900 px-5 py-3 text-sm font-semibold text-white transition hover:bg-gray-700"
          onClick={onBack}
        >
          All playlists
        </button>
        <button
          type="button"
          className="rounded-2xl bg-white/80 px-5 py-3 text-sm font-semibold text-gray-800 transition hover:bg-white"
          onClick={onRefresh}
        >
          Refresh playlists
        </button>
      </div>
    </GlassCard>
  );
}

function YouTubePlaylistsContent({
  status,
  playlists,
  selectedPlaylist,
  playlistItems,
  isLoadingItems,
  missingPlaylistId,
  onConnect,
  onSelectPlaylist,
  onBack,
  onRefresh,
  onDisconnect,
}: YouTubePlaylistsContentProps) {
  if (!status?.isConnected) {
    return <YouTubeDisconnectedState onConnect={onConnect} />;
  }

  if (missingPlaylistId) {
    return <YouTubePlaylistMissingState playlistId={missingPlaylistId} onBack={onBack} onRefresh={onRefresh} />;
  }

  if (selectedPlaylist) {
    return (
      <YouTubePlaylistDetailView
        playlist={selectedPlaylist}
        playlistItems={playlistItems}
        isLoadingItems={isLoadingItems}
        onBack={onBack}
      />
    );
  }

  return (
    <YouTubePlaylistBrowser
      status={status}
      playlists={playlists}
      onRefresh={onRefresh}
      onDisconnect={onDisconnect}
      onSelectPlaylist={onSelectPlaylist}
    />
  );
}

function YouTubePlaylistError({ error }: { error: string | null }) {
  if (!error) {
    return null;
  }

  return (
    <GlassCard className="border-rose-300 bg-rose-50 p-4 text-sm text-rose-700">
      {error}
    </GlassCard>
  );
}

function YouTubePlaylistsLoading({ embedded }: { embedded: boolean }) {
  return embedded ? (
    <GlassCard className="p-6">
      <p className="text-sm text-gray-600">Loading YouTube playlists...</p>
    </GlassCard>
  ) : (
    <PageLoadingState message="Loading YouTube playlists..." />
  );
}

function YouTubePlaylistsLayout({ children, embedded }: { children: ReactNode; embedded: boolean }) {
  if (embedded) {
    return <div className="space-y-5">{children}</div>;
  }

  return (
    <GradientPageShell className="text-gray-900">
      <header>
        <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">YouTube</p>
        <h1 className="mt-1 text-3xl font-bold">Manage playlists</h1>
      </header>
      {children}
    </GradientPageShell>
  );
}

export function YouTubePlaylistsPage({ embedded = false, playlistId = null }: YouTubePlaylistsPageProps) {
  const navigate = useNavigate();
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

  if (isLoading) {
    return <YouTubePlaylistsLoading embedded={embedded} />;
  }

  const missingPlaylistId = isMissingPlaylistRoute({ isConnected, isLoading, playlistId, playlists })
    ? playlistId
    : null;
  return (
    <YouTubePlaylistsLayout embedded={embedded}>
      <YouTubePlaylistError error={error} />
      <YouTubePlaylistsContent
        status={status}
        playlists={playlists}
        selectedPlaylist={selectedPlaylist}
        playlistItems={playlistItems}
        isLoadingItems={isLoadingItems}
        missingPlaylistId={missingPlaylistId}
        onConnect={() => void connect()}
        onSelectPlaylist={handleSelectPlaylist}
        onBack={handleBackToPlaylists}
        onRefresh={() => void refreshPlaylists()}
        onDisconnect={() => void disconnect()}
      />
    </YouTubePlaylistsLayout>
  );
}
