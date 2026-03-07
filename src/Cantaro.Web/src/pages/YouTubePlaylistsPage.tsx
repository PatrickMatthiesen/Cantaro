import { useState, useEffect, useCallback } from 'react';
import type { SVGProps } from 'react';
import { platformManager } from '../platforms';
import type { PlatformAccountStatus, PlatformPlaylist, PlatformSong } from '../platforms';
import { GlassCard, GradientButton } from '../components/ui/GlassComponents';

function YouTubeIcon({ ariaLabel, ...props }: { ariaLabel?: string } & SVGProps<SVGSVGElement>) {
  return (
    <svg
      width="24"
      height="24"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-hidden={!ariaLabel}
      role={ariaLabel ? 'img' : undefined}
      aria-label={ariaLabel}
      {...props}
    >
      <path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z" />
    </svg>
  );
}

interface YouTubePlaylistsPageProps {
  onNavigateHome: () => void;
  onNavigateMatching: () => void;
}

export function YouTubePlaylistsPage({ onNavigateHome, onNavigateMatching }: YouTubePlaylistsPageProps) {
  const [status, setStatus] = useState<PlatformAccountStatus | null>(null);
  const [playlists, setPlaylists] = useState<PlatformPlaylist[]>([]);
  const [selectedPlaylist, setSelectedPlaylist] = useState<PlatformPlaylist | null>(null);
  const [playlistItems, setPlaylistItems] = useState<PlatformSong[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingItems, setIsLoadingItems] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const youtubePlatform = platformManager.getClient('youtube');

  const loadStatus = useCallback(async () => {
    try {
      const statusData = await youtubePlatform.status();
      setStatus(statusData);
      return statusData;
    } catch (err) {
      console.error('Failed to load YouTube status:', err);
      setError('Failed to check YouTube connection status');
      return null;
    }
  }, [youtubePlatform]);

  const loadPlaylists = useCallback(async () => {
    try {
      const playlistsData = await youtubePlatform.playlists(false);
      setPlaylists(playlistsData);
      setError(null);
    } catch (err) {
      console.error('Failed to load playlists:', err);
      setError(err instanceof Error ? err.message : 'Failed to load playlists');
    }
  }, [youtubePlatform]);

  useEffect(() => {
    const init = async () => {
      setIsLoading(true);
      const statusData = await loadStatus();
      if (statusData?.isConnected) {
        await loadPlaylists();
      }
      setIsLoading(false);
    };
    init();
  }, [loadStatus, loadPlaylists]);

  const handleConnect = () => {
    void platformManager.connect('youtube', {
      route: window.location.pathname,
      trigger: 'youtube-page-connect',
    });
  };

  const handleDisconnect = async () => {
    try {
      await platformManager.disconnect('youtube', {
        onSuccess: () => {
          setStatus({ platformId: 'youtube', isConnected: false });
          setPlaylists([]);
          setSelectedPlaylist(null);
          setPlaylistItems([]);
        },
      });
      await loadStatus();
    } catch {
      setError('Failed to disconnect YouTube account');
    }
  };

  const handlePlaylistClick = async (playlist: PlatformPlaylist) => {
    setSelectedPlaylist(playlist);
    setIsLoadingItems(true);
    try {
      const items = await playlist.songs();
      setPlaylistItems(items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load playlist items');
      setPlaylistItems([]);
    }
    setIsLoadingItems(false);
  };

  const handleRefreshPlaylists = async () => {
    try {
      setIsLoading(true);
      const refreshedPlaylists = await platformManager.refreshPlaylists('youtube', false);
      setPlaylists(refreshedPlaylists);
      setSelectedPlaylist(null);
      setPlaylistItems([]);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refresh playlists');
    } finally {
      setIsLoading(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50">
        <GlassCard className="px-6 py-4">
          <p className="text-sm text-gray-700">Loading YouTube playlists…</p>
        </GlassCard>
      </div>
    );
  }

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto max-w-6xl space-y-5 px-6 pt-8 pb-16">
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
              <GradientButton gradient="from-rose-500 to-red-500" onClick={handleDisconnect}>
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

        {!status?.isConnected ? (
          <GlassCard className="p-8">
            <div className="inline-flex items-center gap-2 rounded-full bg-red-50 px-3 py-1 text-xs font-semibold text-red-700 uppercase">
              <YouTubeIcon /> Not connected
            </div>
            <h2 className="mt-4 text-2xl font-semibold">Connect YouTube to begin</h2>
            <p className="mt-2 text-sm text-gray-600">
              Once connected, you can browse playlists and sync them through Cantaro.
            </p>
            <div className="mt-5">
              <GradientButton gradient="from-red-500 to-rose-500" onClick={handleConnect}>
                Connect with YouTube
              </GradientButton>
            </div>
          </GlassCard>
        ) : selectedPlaylist ? (
          <div className="grid gap-4 lg:grid-cols-[320px_1fr]">
            <GlassCard className="p-5">
              <button
                type="button"
                onClick={() => {
                  setSelectedPlaylist(null);
                  setPlaylistItems([]);
                }}
                className="mb-4 text-sm font-semibold text-indigo-700 hover:text-indigo-500"
              >
                ← All playlists
              </button>
              {selectedPlaylist.thumbnailUrl ? (
                <img
                  src={selectedPlaylist.thumbnailUrl}
                  alt={selectedPlaylist.title}
                  className="h-52 w-full rounded-2xl object-cover"
                />
              ) : (
                <div className="flex h-52 w-full items-center justify-center rounded-2xl bg-white/70 text-gray-500">
                  <YouTubeIcon ariaLabel="Playlist thumbnail not available" />
                </div>
              )}
              <h2 className="mt-4 text-xl font-semibold">{selectedPlaylist.title}</h2>
              {selectedPlaylist.description ? (
                <p className="mt-2 text-sm text-gray-600">{selectedPlaylist.description}</p>
              ) : null}
            </GlassCard>

            <GlassCard className="p-4">
              {isLoadingItems ? (
                <p className="p-6 text-sm text-gray-600">Loading playlist items…</p>
              ) : playlistItems.length === 0 ? (
                <p className="p-6 text-sm text-gray-600">This playlist has no items yet.</p>
              ) : (
                <ol className="space-y-2">
                  {playlistItems.map((item, index) => (
                    <li key={item.id} className="flex items-center gap-3 rounded-xl bg-white/70 p-3">
                      <span className="w-6 text-xs font-semibold text-gray-500">{index + 1}</span>
                      {item.thumbnailUrl ? (
                        <img src={item.thumbnailUrl} alt="" className="h-14 w-24 rounded-lg object-cover" />
                      ) : (
                        <div className="h-14 w-24 rounded-lg bg-white" aria-hidden />
                      )}
                      <div className="min-w-0 flex-1">
                        <p className="line-clamp-2 text-sm font-medium text-gray-800">{item.title}</p>
                        {item.artistName ? (
                          <p className="text-xs text-gray-500">{item.artistName}</p>
                        ) : null}
                      </div>
                    </li>
                  ))}
                </ol>
              )}
            </GlassCard>
          </div>
        ) : (
          <div className="space-y-4">
            <GlassCard className="p-5">
              <p className="text-sm text-gray-600">
                Connected as <span className="font-semibold text-gray-900">{status.displayName ?? 'YouTube account'}</span>
              </p>
              <div className="mt-4">
                <GradientButton tone="soft" onClick={() => void handleRefreshPlaylists()}>
                  Refresh playlists
                </GradientButton>
              </div>
            </GlassCard>

            {playlists.length === 0 ? (
              <GlassCard className="p-6">
                <p className="text-sm text-gray-600">No playlists found in this account.</p>
              </GlassCard>
            ) : (
              <section className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
                {playlists.map((playlist) => (
                  <button
                    key={playlist.id}
                    onClick={() => handlePlaylistClick(playlist)}
                    className="group overflow-hidden rounded-3xl border border-white/80 bg-white/70 text-left shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px] transition-transform hover:scale-[1.01]"
                  >
                    {playlist.thumbnailUrl ? (
                      <img src={playlist.thumbnailUrl} alt={playlist.title} className="h-44 w-full object-cover" />
                    ) : (
                      <div className="flex h-44 w-full items-center justify-center bg-white text-gray-500" aria-hidden>
                        <YouTubeIcon />
                      </div>
                    )}
                    <div className="p-4">
                      <h3 className="line-clamp-1 text-lg font-semibold text-gray-900">{playlist.title}</h3>
                      {playlist.description ? (
                        <p className="mt-2 line-clamp-2 text-sm text-gray-600">{playlist.description}</p>
                      ) : null}
                    </div>
                  </button>
                ))}
              </section>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
