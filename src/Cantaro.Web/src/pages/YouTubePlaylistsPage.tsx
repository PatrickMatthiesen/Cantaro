import { useState, useEffect, useCallback } from 'react';
import { youtubeApi } from '../services/youtubeApi';
import type { ConnectedAccountStatus, YouTubePlaylist, YouTubePlaylistItem } from '../services/youtubeApi';

const styles = {
  container: {
    minHeight: '100vh',
    background: 'linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%)',
    padding: '2rem',
    color: '#fff',
  },
  header: {
    textAlign: 'center' as const,
    marginBottom: '2rem',
  },
  title: {
    fontSize: '2.5rem',
    fontWeight: 700,
    background: 'linear-gradient(90deg, #ff6b6b, #ee5a24)',
    WebkitBackgroundClip: 'text',
    WebkitTextFillColor: 'transparent',
    marginBottom: '0.5rem',
  },
  subtitle: {
    fontSize: '1rem',
    color: '#a0aec0',
  },
  connectionCard: {
    background: 'rgba(255, 255, 255, 0.05)',
    borderRadius: '16px',
    padding: '2rem',
    maxWidth: '600px',
    margin: '0 auto 2rem',
    backdropFilter: 'blur(10px)',
    border: '1px solid rgba(255, 255, 255, 0.1)',
    textAlign: 'center' as const,
  },
  connectedBadge: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '0.5rem',
    padding: '0.5rem 1rem',
    background: 'rgba(16, 185, 129, 0.2)',
    border: '1px solid rgba(16, 185, 129, 0.4)',
    borderRadius: '9999px',
    color: '#10b981',
    fontSize: '0.875rem',
    marginBottom: '1rem',
  },
  accountInfo: {
    marginBottom: '1rem',
    color: '#e2e8f0',
  },
  connectButton: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '0.75rem',
    padding: '1rem 2rem',
    background: 'linear-gradient(135deg, #ff0000 0%, #cc0000 100%)',
    color: '#fff',
    border: 'none',
    borderRadius: '12px',
    fontSize: '1.1rem',
    fontWeight: 600,
    cursor: 'pointer',
    transition: 'transform 0.2s, box-shadow 0.2s',
    boxShadow: '0 4px 15px rgba(255, 0, 0, 0.3)',
  },
  disconnectButton: {
    padding: '0.5rem 1rem',
    background: 'transparent',
    color: '#f56565',
    border: '1px solid #f56565',
    borderRadius: '8px',
    fontSize: '0.875rem',
    cursor: 'pointer',
    transition: 'all 0.2s',
  },
  playlistsGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))',
    gap: '1.5rem',
    maxWidth: '1400px',
    margin: '0 auto',
  },
  playlistCard: {
    background: 'rgba(255, 255, 255, 0.05)',
    borderRadius: '16px',
    overflow: 'hidden',
    cursor: 'pointer',
    transition: 'transform 0.2s, box-shadow 0.2s',
    border: '1px solid rgba(255, 255, 255, 0.1)',
  },
  playlistThumbnail: {
    width: '100%',
    height: '180px',
    objectFit: 'cover' as const,
    background: 'linear-gradient(135deg, #2d3748 0%, #1a202c 100%)',
  },
  playlistInfo: {
    padding: '1rem',
  },
  playlistTitle: {
    fontSize: '1.1rem',
    fontWeight: 600,
    marginBottom: '0.5rem',
    color: '#fff',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap' as const,
  },
  playlistMeta: {
    fontSize: '0.875rem',
    color: '#a0aec0',
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
  },
  backButton: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '0.5rem',
    padding: '0.75rem 1.5rem',
    background: 'rgba(255, 255, 255, 0.1)',
    color: '#fff',
    border: 'none',
    borderRadius: '8px',
    fontSize: '1rem',
    cursor: 'pointer',
    marginBottom: '1.5rem',
  },
  playlistHeader: {
    background: 'rgba(255, 255, 255, 0.05)',
    borderRadius: '16px',
    padding: '1.5rem',
    marginBottom: '1.5rem',
    display: 'flex',
    gap: '1.5rem',
    alignItems: 'flex-start',
  },
  playlistHeaderImage: {
    width: '200px',
    height: '200px',
    borderRadius: '12px',
    objectFit: 'cover' as const,
  },
  playlistHeaderInfo: {
    flex: 1,
  },
  playlistHeaderTitle: {
    fontSize: '2rem',
    fontWeight: 700,
    marginBottom: '0.5rem',
  },
  playlistHeaderMeta: {
    color: '#a0aec0',
    fontSize: '1rem',
  },
  itemsList: {
    background: 'rgba(255, 255, 255, 0.05)',
    borderRadius: '16px',
    overflow: 'hidden',
  },
  itemRow: {
    display: 'flex',
    alignItems: 'center',
    gap: '1rem',
    padding: '0.75rem 1rem',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
    transition: 'background 0.2s',
  },
  itemNumber: {
    width: '2rem',
    textAlign: 'center' as const,
    color: '#718096',
    fontSize: '0.875rem',
  },
  itemThumbnail: {
    width: '80px',
    height: '45px',
    borderRadius: '4px',
    objectFit: 'cover' as const,
    background: '#2d3748',
  },
  itemInfo: {
    flex: 1,
    minWidth: 0,
  },
  itemTitle: {
    fontSize: '0.95rem',
    color: '#fff',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap' as const,
  },
  itemChannel: {
    fontSize: '0.8rem',
    color: '#718096',
  },
  loading: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    height: '200px',
    fontSize: '1.2rem',
    color: '#a0aec0',
  },
  error: {
    background: 'rgba(245, 101, 101, 0.1)',
    border: '1px solid rgba(245, 101, 101, 0.3)',
    borderRadius: '8px',
    padding: '1rem',
    color: '#fc8181',
    textAlign: 'center' as const,
    marginBottom: '1rem',
  },
  emptyState: {
    textAlign: 'center' as const,
    padding: '3rem',
    color: '#a0aec0',
  },
  navBar: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: '1rem 2rem',
    marginBottom: '1rem',
  },
  homeLink: {
    color: '#a0aec0',
    textDecoration: 'none',
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
    fontSize: '0.9rem',
    cursor: 'pointer',
    background: 'none',
    border: 'none',
  },
};

function YouTubeIcon() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
      <path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z"/>
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
      <div style={styles.container}>
        <div style={styles.loading}>
          <span>Loading...</span>
        </div>
      </div>
    );
  }

  return (
    <div style={styles.container}>
      <nav style={styles.navBar}>
        <button 
          style={styles.homeLink}
          onClick={onNavigateHome}
        >
          ← Back to Home
        </button>
      </nav>

      <header style={styles.header}>
        <h1 style={styles.title}>YouTube Playlists</h1>
        <p style={styles.subtitle}>View and manage your YouTube playlists</p>
      </header>

      {error && (
        <div style={{ maxWidth: '600px', margin: '0 auto 1rem' }}>
          <div style={styles.error}>{error}</div>
        </div>
      )}

      {!status?.isConnected ? (
        <div style={styles.connectionCard}>
          <h2 style={{ marginBottom: '1rem', fontSize: '1.5rem' }}>Connect Your YouTube Account</h2>
          <p style={{ color: '#a0aec0', marginBottom: '1.5rem' }}>
            Link your YouTube account to view and manage your playlists
          </p>
          <button 
            style={styles.connectButton}
            onClick={handleConnect}
            onMouseOver={(e) => {
              e.currentTarget.style.transform = 'translateY(-2px)';
              e.currentTarget.style.boxShadow = '0 6px 20px rgba(255, 0, 0, 0.4)';
            }}
            onMouseOut={(e) => {
              e.currentTarget.style.transform = 'translateY(0)';
              e.currentTarget.style.boxShadow = '0 4px 15px rgba(255, 0, 0, 0.3)';
            }}
          >
            <YouTubeIcon />
            Connect with YouTube
          </button>
        </div>
      ) : selectedPlaylist ? (
        <div style={{ maxWidth: '1000px', margin: '0 auto' }}>
          <button style={styles.backButton} onClick={handleBackToPlaylists}>
            ← Back to Playlists
          </button>

          <div style={styles.playlistHeader}>
            {selectedPlaylist.thumbnailUrl && (
              <img
                src={selectedPlaylist.thumbnailUrl}
                alt={selectedPlaylist.title}
                style={styles.playlistHeaderImage}
              />
            )}
            <div style={styles.playlistHeaderInfo}>
              <h2 style={styles.playlistHeaderTitle}>{selectedPlaylist.title}</h2>
              <p style={styles.playlistHeaderMeta}>
                {selectedPlaylist.itemCount} videos
                {selectedPlaylist.description && (
                  <span style={{ display: 'block', marginTop: '0.5rem', opacity: 0.8 }}>
                    {selectedPlaylist.description}
                  </span>
                )}
              </p>
            </div>
          </div>

          {isLoadingItems ? (
            <div style={styles.loading}>Loading videos...</div>
          ) : playlistItems.length === 0 ? (
            <div style={styles.emptyState}>
              <p>No videos in this playlist</p>
            </div>
          ) : (
            <div style={styles.itemsList}>
              {playlistItems.map((item, index) => (
                <div 
                  key={item.videoId} 
                  style={styles.itemRow}
                  onMouseOver={(e) => {
                    e.currentTarget.style.background = 'rgba(255, 255, 255, 0.05)';
                  }}
                  onMouseOut={(e) => {
                    e.currentTarget.style.background = 'transparent';
                  }}
                >
                  <span style={styles.itemNumber}>{index + 1}</span>
                  {item.thumbnailUrl && (
                    <img 
                      src={item.thumbnailUrl} 
                      alt="" 
                      style={styles.itemThumbnail}
                    />
                  )}
                  <div style={styles.itemInfo}>
                    <div style={styles.itemTitle}>{item.title}</div>
                    {item.channelTitle && (
                      <div style={styles.itemChannel}>{item.channelTitle}</div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      ) : (
        <>
          <div style={styles.connectionCard}>
            <div style={styles.connectedBadge}>
              <span>✓</span> Connected
            </div>
            <div style={styles.accountInfo}>
              <strong>{status.displayName}</strong>
            </div>
            <button 
              style={styles.disconnectButton} 
              onClick={handleDisconnect}
              onMouseOver={(e) => {
                e.currentTarget.style.background = 'rgba(245, 101, 101, 0.1)';
              }}
              onMouseOut={(e) => {
                e.currentTarget.style.background = 'transparent';
              }}
            >
              Disconnect
            </button>
          </div>

          {playlists.length === 0 ? (
            <div style={styles.emptyState}>
              <p>No playlists found on your YouTube account</p>
            </div>
          ) : (
            <div style={styles.playlistsGrid}>
              {playlists.map((playlist) => (
                <div
                  key={playlist.id}
                  style={styles.playlistCard}
                  onClick={() => handlePlaylistClick(playlist)}
                  onMouseOver={(e) => {
                    e.currentTarget.style.transform = 'translateY(-4px)';
                    e.currentTarget.style.boxShadow = '0 8px 30px rgba(0, 0, 0, 0.3)';
                  }}
                  onMouseOut={(e) => {
                    e.currentTarget.style.transform = 'translateY(0)';
                    e.currentTarget.style.boxShadow = 'none';
                  }}
                >
                  {playlist.thumbnailUrl ? (
                    <img
                      src={playlist.thumbnailUrl}
                      alt={playlist.title}
                      style={styles.playlistThumbnail}
                    />
                  ) : (
                    <div style={{
                      ...styles.playlistThumbnail,
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}>
                      <YouTubeIcon />
                    </div>
                  )}
                  <div style={styles.playlistInfo}>
                    <h3 style={styles.playlistTitle}>{playlist.title}</h3>
                    <div style={styles.playlistMeta}>
                      <span>{playlist.itemCount} videos</span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
