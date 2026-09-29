import { MusicPlatformIcon, type PlatformPlaylist } from '@cantaro/client-shared/music';
import { MusicPlatformPlaylistSyncAction, MusicPlatformPlaylistSyncError } from './MusicPlatformPlaylistSyncAction';
import { usePlatformPlaylistSync } from './usePlatformPlaylistSync';

type PlatformId = 'spotify' | 'youtube';

const platformNames = { spotify: 'Spotify', youtube: 'YouTube' };
const platformColors = { spotify: 'text-[#1ed760]', youtube: 'text-[#ff0000]' };

type MusicPlatformHeaderProps = {
  platformId: PlatformId;
  accountName?: string | null;
  isConnected: boolean;
  needsReconnect: boolean;
  isLoading: boolean;
  onConnect: () => void;
  onRefresh: () => void;
  onDisconnect: () => void;
};

export function MusicPlatformHeader(props: MusicPlatformHeaderProps) {
  const { platformId, accountName, isConnected, needsReconnect } = props;
  return (
    <section className="bg-surface-subtle p-5">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex min-w-0 items-center gap-3">
          <MusicPlatformIcon platformId={platformId} className={`h-9 w-9 shrink-0 ${platformColors[platformId]}`} />
          <div className="min-w-0">
            <h1 className="text-2xl font-black text-content">{platformNames[platformId]}</h1>
            {isConnected && accountName ? <p className="truncate text-xs font-medium text-content-muted">{accountName}</p> : null}
          </div>
        </div>
        <MusicPlatformActions {...props} />
      </div>
      {needsReconnect ? <p role="status" className="mt-4 text-sm font-semibold text-warning-content">Your connection expired. Reconnect to load playlists.</p> : null}
    </section>
  );
}

function MusicPlatformActions({ isConnected, needsReconnect, isLoading, onConnect, onRefresh, onDisconnect }: MusicPlatformHeaderProps) {
  return (
    <div className="flex flex-wrap gap-2">
      {isConnected && !needsReconnect ? (
        <button type="button" disabled={isLoading} onClick={onRefresh} className="min-h-11 border border-border-strong bg-surface px-4 text-sm font-black text-content transition hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">
          Refresh
        </button>
      ) : (
        <button type="button" disabled={isLoading} onClick={onConnect} className="min-h-11 bg-personal-accent px-4 text-sm font-black text-personal-accent-content transition hover:bg-personal-accent-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">
          {needsReconnect ? 'Reconnect' : 'Connect'}
        </button>
      )}
      {isConnected ? (
        <button type="button" disabled={isLoading} onClick={onDisconnect} className="min-h-11 px-4 text-sm font-black text-danger-content transition hover:bg-danger-surface focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">
          Disconnect
        </button>
      ) : null}
    </div>
  );
}

function MusicPlatformPlaylistCard({ platformId, playlist, onSelect, sync }: {
  sync: ReturnType<typeof usePlatformPlaylistSync>;
  platformId: PlatformId;
  playlist: PlatformPlaylist;
  onSelect: (playlist: PlatformPlaylist) => void;
}) {
  const externalUrl = playlist.externalUrl ?? (platformId === 'youtube' ? `https://www.youtube.com/playlist?list=${encodeURIComponent(playlist.id)}` : null);
  return (
    <article className="min-w-0 overflow-hidden bg-surface-subtle">
      <button type="button" onClick={() => onSelect(playlist)} className="group block w-full text-left transition hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus focus-visible:-outline-offset-2">
        <MusicPlatformArtwork platformId={platformId} playlist={playlist} />
        <span className="sr-only">Open {playlist.title}</span>
      </button>
      <div className="flex items-center gap-1 px-3 pt-2">
        <button type="button" onClick={() => onSelect(playlist)} className="min-w-0 flex-1 py-1 text-left focus-visible:outline-2 focus-visible:outline-focus">
          <span className="block truncate text-sm font-black text-content" title={playlist.title}>{playlist.title}</span>
          <span className="mt-1 block truncate text-xs font-medium text-content-muted">
            {playlist.itemCount.toLocaleString()} {playlist.itemCount === 1 ? 'track' : 'tracks'}{playlist.ownerName ? ` · ${playlist.ownerName}` : ''}
          </span>
        </button>
        <MusicPlatformPlaylistSyncAction playlistId={playlist.id} title={playlist.title} sync={sync} />
      </div>
      <MusicPlatformPlaylistSyncError playlistId={playlist.id} sync={sync} />
      {externalUrl ? (
        <a href={externalUrl} target="_blank" rel="noopener noreferrer" aria-label={`Open ${playlist.title} on ${platformNames[platformId]}`} className="flex min-h-11 items-center gap-2 px-3 pb-2 text-xs font-semibold text-content-muted transition hover:text-content focus-visible:outline-2 focus-visible:outline-focus focus-visible:-outline-offset-2">
          <MusicPlatformIcon platformId={platformId} className={`h-5 w-5 shrink-0 ${platformColors[platformId]}`} />
          Open in {platformNames[platformId]}
        </a>
      ) : null}
    </article>
  );
}

function MusicPlatformArtwork({ platformId, playlist }: { platformId: PlatformId; playlist: PlatformPlaylist }) {
  if (playlist.thumbnailUrl) {
    return <img src={playlist.thumbnailUrl} alt="" loading="lazy" className="block h-auto w-full bg-surface transition group-hover:brightness-110" />;
  }
  return (
    <span className={`flex w-full items-center justify-center bg-surface text-content-muted ${platformId === 'youtube' ? 'aspect-video' : 'aspect-square'}`}>
      <MusicPlatformIcon platformId={platformId} className="h-10 w-10" />
    </span>
  );
}

export function MusicPlatformPlaylistGrid({ platformId, playlists, onSelect }: {
  platformId: PlatformId;
  playlists: PlatformPlaylist[];
  onSelect: (playlist: PlatformPlaylist) => void;
}) {
  const sync = usePlatformPlaylistSync(platformId);
  if (playlists.length === 0) {
    return <p className="py-6 text-sm font-semibold text-content-muted">No playlists found.</p>;
  }
  return (
    <>
      {sync.loadError ? <div role="alert" className="flex flex-wrap items-center gap-3 text-sm text-danger-content"><span>{sync.loadError}</span><button type="button" onClick={() => void sync.reload()} className="min-h-11 px-3 font-bold underline focus-visible:outline-2 focus-visible:outline-focus">Retry</button></div> : null}
      <section aria-label={`${platformNames[platformId]} playlists`} className="grid grid-cols-1 items-start gap-4 min-[480px]:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {playlists.map((playlist) => <MusicPlatformPlaylistCard key={playlist.id} platformId={platformId} playlist={playlist} onSelect={onSelect} sync={sync} />)}
      </section>
    </>
  );
}
