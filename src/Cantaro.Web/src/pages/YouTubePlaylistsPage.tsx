import { GlassCard, GradientButton, GradientPageShell, PageLoadingState } from '../components/ui/GlassComponents';
import {
  YouTubeDisconnectedState,
  YouTubePlaylistBrowser,
  YouTubePlaylistDetailView,
} from '../components/youtube/YouTubePlaylistViews';
import { useYouTubePlaylistsState } from '../components/youtube/useYouTubePlaylistsState';
import type { PlatformAccountStatus, PlatformPlaylist, PlatformSong } from '../platforms';

interface YouTubePlaylistsPageProps {
  onNavigateHome: () => void;
  onNavigateMatching: () => void;
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
      onSelectPlaylist={onSelectPlaylist}
    />
  );
}

export function YouTubePlaylistsPage({ onNavigateHome, onNavigateMatching }: YouTubePlaylistsPageProps) {
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
        <div className="flex gap-2">
          <GradientButton tone="soft" onClick={onNavigateHome}>
            Back to home
          </GradientButton>
          <GradientButton tone="soft" onClick={onNavigateMatching}>
            Review matches
          </GradientButton>
          {status?.isConnected ? (
            <GradientButton gradient="from-rose-500 to-red-500" onClick={() => void disconnect()}>
              Disconnect
            </GradientButton>
          ) : null}
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
        onSelectPlaylist={(playlist) => void selectPlaylist(playlist)}
        onBack={clearSelectedPlaylist}
        onRefresh={() => void refreshPlaylists()}
      />
    </GradientPageShell>
  );
}
