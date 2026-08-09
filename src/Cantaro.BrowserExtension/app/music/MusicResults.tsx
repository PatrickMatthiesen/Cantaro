import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { MusicState } from './MusicState';

export function MusicResults({ songs, hasLibrarySongs, onSelect }: {
  songs: MusicLibrarySong[];
  hasLibrarySongs: boolean;
  onSelect: (song: MusicLibrarySong) => void;
}) {
  if (!hasLibrarySongs) {
    return <MusicState title="No songs yet" detail="Sync a playlist to build your canonical music library." />;
  }
  if (songs.length === 0) {
    return <MusicState title="No matches" detail="Try another title, artist, or album." />;
  }

  return (
    <ul className="space-y-1.5">
      {songs.map((song) => (
        <li key={song.id}>
          <button
            type="button"
            className="group flex w-full items-center gap-3 rounded-xl border border-transparent bg-surface-translucent p-2 text-left transition-colors hover:border-border-strong hover:bg-surface-hover focus-visible:border-focus focus-visible:ring-2 focus-visible:ring-accent-soft focus-visible:outline-none"
            onClick={() => onSelect(song)}
          >
            {song.thumbnailUrl ? (
              <img src={song.thumbnailUrl} alt="" className="size-11 rounded-lg object-cover" />
            ) : (
              <div className="size-11 rounded-lg bg-accent-soft" />
            )}
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-bold text-content">{song.title}</span>
              <span className="block truncate text-xs text-content-muted">
                {song.artist || 'Unknown artist'} · {song.playlists.length} playlist{song.playlists.length === 1 ? '' : 's'}
              </span>
            </span>
            <span className="text-lg text-accent opacity-0 transition-opacity group-hover:opacity-100" aria-hidden>›</span>
          </button>
        </li>
      ))}
    </ul>
  );
}
