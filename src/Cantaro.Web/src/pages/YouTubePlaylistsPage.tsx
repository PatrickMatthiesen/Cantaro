import { useState, useEffect, useCallback } from 'react';
import { youtubeApi } from '../services/youtubeApi';
import type { ConnectedAccountStatus, YouTubePlaylist, YouTubePlaylistItem } from '../services/youtubeApi';

function YouTubeIcon() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor" aria-hidden>
      <path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z" />
    </svg>
  );
}

interface YouTubePlaylistsPageProps {
  onNavigateHome: () => void;
}

export function YouTubePlaylistsPage({ onNavigateHome }: YouTubePlaylistsPageProps) {
  const [status, setStatus] = useState<ConnectedAccountStatus | null>(null);
  const [playlists, setPlaylists] = useState<YouTubePlaylist[]>([]);
  const [selectedPlaylist, setSelectedPlaylist] = useState<YouTubePlaylist | null>(null);
  const [playlistItems, setPlaylistItems] = useState<YouTubePlaylistItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingItems, setIsLoadingItems] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadStatus = useCallback(async () => {
    try {
      const statusData = await youtubeApi.getStatus();
      setStatus(statusData);
      return statusData;
    } catch (err) {
      console.error('Failed to load YouTube status:', err);
      setError('Failed to check YouTube connection status');
      return null;
    }
  }, []);

  const loadPlaylists = useCallback(async () => {
    try {
      const playlistsData = await youtubeApi.getPlaylists();
      setPlaylists(playlistsData);
      setError(null);
    } catch (err) {
      console.error('Failed to load playlists:', err);
      setError(err instanceof Error ? err.message : 'Failed to load playlists');
    }
  }, []);

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
    window.location.href = youtubeApi.getConnectUrl('/youtube');
  };

  const handleDisconnect = async () => {
    try {
      await youtubeApi.disconnect();
      setStatus({ isConnected: false });
      setPlaylists([]);
    } catch {
      setError('Failed to disconnect YouTube account');
    }
  };

  const handlePlaylistClick = async (playlist: YouTubePlaylist) => {
    setSelectedPlaylist(playlist);
    setIsLoadingItems(true);
    try {
      const items = await youtubeApi.getPlaylistItems(playlist.id);
      setPlaylistItems(items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load playlist items');
      setPlaylistItems([]);
    }
    setIsLoadingItems(false);
  };

  const handleBackToPlaylists = () => {
    setSelectedPlaylist(null);
    setPlaylistItems([]);
  };

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-950 text-slate-200">
        <div className="flex items-center gap-4 rounded-3xl border border-white/10 bg-slate-900/60 px-8 py-6 backdrop-blur-xl">
          <span className="h-3 w-3 animate-pulse rounded-full bg-rose-400" aria-hidden />
          <p className="text-lg font-medium tracking-tight">Fetching your YouTube data…</p>
        </div>
      </div>
    );
  }

  const EmptyState = ({ message }: { message: string }) => (
    <div className="rounded-[28px] border border-white/10 bg-white/5 p-10 text-center text-slate-300">
      <p>{message}</p>
    </div>
  );

  return (
    <div className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-50">
      <div
        className="absolute inset-0 bg-[radial-gradient(circle_at_top,_rgba(248,113,113,0.18),_transparent_55%),_radial-gradient(circle_at_80%_0,_rgba(59,130,246,0.2),_transparent_50%),_#050714]"
        aria-hidden
      />
      <div className="absolute inset-y-0 left-1/2 h-[600px] w-[600px] -translate-x-1/2 rounded-full bg-rose-500/10 blur-[220px]" aria-hidden />

      <div className="relative z-10 px-6 pb-16 pt-6">
        <div className="mx-auto max-w-6xl space-y-8">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <button
              type="button"
              onClick={onNavigateHome}
              className="inline-flex items-center gap-3 rounded-full border border-white/15 px-5 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-white/40"
            >
              <span className="text-lg">←</span>
              Back to Cantaro
            </button>
            <span className="rounded-full border border-rose-400/40 bg-rose-500/10 px-4 py-2 text-xs font-semibold uppercase tracking-[0.4em] text-rose-100">
              YouTube workspace
            </span>
          </div>

          <header className="space-y-3 text-left">
            <p className="text-xs uppercase tracking-[0.5em] text-rose-100/80">Adapter view</p>
            <h1 className="text-4xl font-semibold text-white">YouTube playlists</h1>
            <p className="max-w-3xl text-sm text-slate-300">
              Inspect OAuth status, browse playlists, and drill into entries as Cantaro maps each video back to a canonical TrackID.
            </p>
          </header>

          {error && (
            <div className="rounded-3xl border border-rose-500/40 bg-rose-500/10 px-6 py-4 text-sm text-rose-200">
              {error}
            </div>
          )}

          {!status?.isConnected ? (
            <section className="rounded-[32px] border border-white/10 bg-slate-900/80 p-10 text-left">
              <div className="inline-flex items-center gap-3 rounded-full border border-white/15 bg-white/5 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200">
                <YouTubeIcon /> Not connected
              </div>
              <h2 className="mt-6 text-3xl font-semibold text-white">Connect your YouTube account</h2>
              <p className="mt-4 max-w-2xl text-sm text-slate-300">
                We use Authorization Code flow and store refresh tokens encrypted at rest inside Cantaro—never in the browser or extension.
              </p>
              <div className="mt-8 grid gap-4 md:grid-cols-3">
                {['Secure OAuth redirect', 'Encrypted refresh token', 'Adapter-level rate limiting'].map((item) => (
                  <div key={item} className="rounded-2xl border border-white/10 bg-white/5 px-4 py-5 text-sm text-slate-200">
                    {item}
                  </div>
                ))}
              </div>
              <div className="mt-10 flex flex-wrap items-center gap-4">
                <button
                  onClick={handleConnect}
                  className="inline-flex items-center justify-center gap-3 rounded-2xl bg-gradient-to-r from-red-500 to-rose-500 px-6 py-3 text-base font-semibold text-white shadow-[0_20px_60px_rgba(244,63,94,0.45)] transition hover:translate-y-0.5"
                >
                  <YouTubeIcon />
                  Connect with YouTube
                </button>
                <p className="text-xs text-slate-400">You will be redirected to accounts.google.com</p>
              </div>
            </section>
          ) : selectedPlaylist ? (
            <section className="space-y-6">
              <button
                type="button"
                onClick={handleBackToPlaylists}
                className="inline-flex items-center gap-3 rounded-full border border-white/15 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-white/40"
              >
                ← All playlists
              </button>

              <div className="grid gap-6 lg:grid-cols-[320px_1fr]">
                <div className="rounded-[28px] border border-white/10 bg-white/5 p-6 text-left">
                  {selectedPlaylist.thumbnailUrl ? (
                    <img
                      src={selectedPlaylist.thumbnailUrl}
                      alt={selectedPlaylist.title}
                      className="h-56 w-full rounded-2xl object-cover"
                    />
                  ) : (
                    <div className="flex h-56 w-full items-center justify-center rounded-2xl bg-white/5">
                      <YouTubeIcon />
                    </div>
                  )}
                  <h2 className="mt-6 text-2xl font-semibold text-white">{selectedPlaylist.title}</h2>
                  <p className="mt-2 text-sm text-slate-300">
                    {selectedPlaylist.itemCount} videos ·{' '}
                    {selectedPlaylist.publishedAt
                      ? new Date(selectedPlaylist.publishedAt).toLocaleDateString()
                      : 'Unknown publish date'}
                  </p>
                  {selectedPlaylist.description && (
                    <p className="mt-3 text-sm text-slate-400">
                      {selectedPlaylist.description}
                    </p>
                  )}
                </div>

                <div className="rounded-[28px] border border-white/10 bg-slate-900/70 p-4 backdrop-blur">
                  {isLoadingItems ? (
                    <div className="flex h-64 items-center justify-center text-slate-300">
                      Loading playlist items…
                    </div>
                  ) : playlistItems.length === 0 ? (
                    <EmptyState message="No videos in this playlist" />
                  ) : (
                    <ol className="space-y-2 text-left">
                      {playlistItems.map((item, index) => (
                        <li
                          key={item.videoId}
                          className="flex items-center gap-4 rounded-2xl border border-white/5 bg-white/5 p-3"
                        >
                          <span className="text-xs font-semibold uppercase tracking-[0.4em] text-slate-400">
                            {String(index + 1).padStart(2, '0')}
                          </span>
                          {item.thumbnailUrl ? (
                            <img
                              src={item.thumbnailUrl}
                              alt=""
                              className="h-16 w-28 rounded-xl object-cover"
                            />
                          ) : (
                            <div className="h-16 w-28 rounded-xl bg-white/10" aria-hidden />
                          )}
                          <div className="min-w-0 flex-1">
                            <p className="text-sm font-semibold text-white line-clamp-2">{item.title}</p>
                            {item.channelTitle && (
                              <p className="text-xs text-slate-400">{item.channelTitle}</p>
                            )}
                          </div>
                        </li>
                      ))}
                    </ol>
                  )}
                </div>
              </div>
            </section>
          ) : (
            <section className="space-y-6">
              <div className="rounded-[32px] border border-white/10 bg-white/5 p-6 text-left">
                <div className="flex flex-wrap items-center justify-between gap-4">
                  <div>
                    <p className="text-xs uppercase tracking-[0.5em] text-emerald-300/80">Connected</p>
                    <h2 className="mt-2 text-2xl font-semibold text-white">
                      {status.displayName ?? 'YouTube account'}
                    </h2>
                    <p className="text-sm text-slate-400">
                      External ID: {status.externalAccountId ?? 'Unavailable'}
                    </p>
                  </div>
                  <button
                    onClick={handleDisconnect}
                    className="rounded-full border border-white/15 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-white/40"
                  >
                    Disconnect
                  </button>
                </div>
                {status.connectedAt && (
                  <p className="mt-4 text-xs uppercase tracking-[0.4em] text-slate-400">
                    Connected · {new Date(status.connectedAt).toLocaleString()}
                  </p>
                )}
              </div>

              {playlists.length === 0 ? (
                <EmptyState message="No playlists found on your YouTube account." />
              ) : (
                <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
                  {playlists.map((playlist) => (
                    <button
                      key={playlist.id}
                      onClick={() => handlePlaylistClick(playlist)}
                      className="group overflow-hidden rounded-[28px] border border-white/10 bg-slate-900/60 text-left transition hover:-translate-y-1 hover:border-white/30"
                    >
                      {playlist.thumbnailUrl ? (
                        <img
                          src={playlist.thumbnailUrl}
                          alt={playlist.title}
                          className="h-48 w-full object-cover"
                        />
                      ) : (
                        <div className="flex h-48 w-full items-center justify-center bg-white/5">
                          <YouTubeIcon />
                        </div>
                      )}
                      <div className="space-y-3 px-6 py-5">
                        <h3 className="text-lg font-semibold text-white line-clamp-1">{playlist.title}</h3>
                        <p className="text-sm text-slate-400">{playlist.itemCount} videos</p>
                        {playlist.description && (
                          <p className="text-xs text-slate-500 line-clamp-2">{playlist.description}</p>
                        )}
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </section>
          )}
        </div>
      </div>
    </div>
  );
}
