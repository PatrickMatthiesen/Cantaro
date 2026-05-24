import { useEffect } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { GlassCard, GradientPageShell, PageLoadingState } from '@cantaro/client-shared/ui';
import {
  YouTubeDisconnectedState,
  YouTubePlaylistBrowser,
  YouTubePlaylistDetailView,
} from '@cantaro/client-shared/music';
import { useYouTubePlaylistsState } from '@cantaro/client-shared/music';
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

  useEffect(() => {
    if (!status?.isConnected || isLoading) {
      return;
    }

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
  }, [clearSelectedPlaylist, isLoading, playlistId, playlists, selectPlaylist, selectedPlaylist, status?.isConnected]);

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

  const content = (
    <>
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
    </>
  );

  if (isLoading) {
    return embedded ? (
      <GlassCard className="p-6">
        <p className="text-sm text-gray-600">Loading YouTube playlists…</p>
      </GlassCard>
    ) : (
      <PageLoadingState message="Loading YouTube playlists…" />
    );
  }

  if (embedded) {
    return <div className="space-y-5">{content}</div>;
  }

  return (
    <GradientPageShell className="text-gray-900">
      <header>
        <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">YouTube</p>
        <h1 className="mt-1 text-3xl font-bold">Manage playlists</h1>
      </header>
      {content}
    </GradientPageShell>
  );
}
