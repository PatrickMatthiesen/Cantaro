import { useEffect, type ReactNode } from 'react';
import { GlassCard, GradientPageShell, PageLoadingState } from '@cantaro/client-shared/ui';
import {
  YouTubeDisconnectedState,
  YouTubePlaylistBrowser,
  YouTubePlaylistDetailView,
} from '@cantaro/client-shared/music';
import { useYouTubePlaylistsState } from '@cantaro/client-shared/music';
import type { PlatformAccountStatus, PlatformPlaylist, PlatformSong } from '@cantaro/client-shared/music';

interface YouTubePlaylistsPageProps {
  navigation?: ReactNode;
}

function resolvePlaylistIdFromPath(): string | null {
  const match = window.location.pathname.match(/^\/youtube\/playlists\/([^/]+)$/);
  return match ? decodeURIComponent(match[1]) : null;
}

interface YouTubePlaylistsContentProps {
  status: PlatformAccountStatus | null;
  playlists: PlatformPlaylist[];
  selectedPlaylist: PlatformPlaylist | null;
  playlistItems: PlatformSong[];
  isLoadingItems: boolean;
  onConnect: () => void;
  onSelectPlaylist: (playlist: PlatformPlaylist) => void;
  onBack: () => void;
  onRefresh: () => void;
  onDisconnect: () => void;
}

function YouTubePlaylistsContent({
  status,
  playlists,
  selectedPlaylist,
  playlistItems,
  isLoadingItems,
  onConnect,
  onSelectPlaylist,
  onBack,
  onRefresh,
  onDisconnect,
}: YouTubePlaylistsContentProps) {
  if (!status?.isConnected) {
    return <YouTubeDisconnectedState onConnect={onConnect} />;
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

export function YouTubePlaylistsPage({ navigation }: YouTubePlaylistsPageProps) {
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

  useEffect(() => {
    if (!status?.isConnected || isLoading) {
      return;
    }

    const playlistId = resolvePlaylistIdFromPath();
    if (!playlistId) {
      if (selectedPlaylist) {
        clearSelectedPlaylist();
      }
      return;
    }

    if (selectedPlaylist?.id === playlistId) {
      return;
    }

    const playlist = playlists.find((item) => item.id === playlistId);
    if (playlist) {
      void selectPlaylist(playlist);
    }
  }, [clearSelectedPlaylist, isLoading, playlists, selectPlaylist, selectedPlaylist, status?.isConnected]);

  useEffect(() => {
    const handlePopState = () => {
      const playlistId = resolvePlaylistIdFromPath();
      if (!playlistId) {
        clearSelectedPlaylist();
        return;
      }

      const playlist = playlists.find((item) => item.id === playlistId);
      if (playlist) {
        void selectPlaylist(playlist);
      }
    };

    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, [clearSelectedPlaylist, playlists, selectPlaylist]);

  const handleSelectPlaylist = (playlist: PlatformPlaylist) => {
    window.history.pushState({}, '', `/youtube/playlists/${encodeURIComponent(playlist.id)}`);
    void selectPlaylist(playlist);
  };

  const handleBackToPlaylists = () => {
    window.history.pushState({}, '', '/youtube');
    clearSelectedPlaylist();
  };

  if (isLoading) {
    return <PageLoadingState message="Loading YouTube playlists…" />;
  }

  return (
    <GradientPageShell className="text-gray-900">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">YouTube</p>
          <h1 className="mt-1 text-3xl font-bold">Manage playlists</h1>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          {navigation}
        </div>
      </header>

      {error ? (
        <GlassCard className="border-rose-300 bg-rose-50 p-4 text-sm text-rose-700">
          {error}
        </GlassCard>
      ) : null}

      <YouTubePlaylistsContent
        status={status}
        playlists={playlists}
        selectedPlaylist={selectedPlaylist}
        playlistItems={playlistItems}
        isLoadingItems={isLoadingItems}
        onConnect={() => void connect()}
        onSelectPlaylist={handleSelectPlaylist}
        onBack={handleBackToPlaylists}
        onRefresh={() => void refreshPlaylists()}
        onDisconnect={() => void disconnect()}
      />
    </GradientPageShell>
  );
}
