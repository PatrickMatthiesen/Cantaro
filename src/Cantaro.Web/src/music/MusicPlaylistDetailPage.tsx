import { type MusicLibraryPlaylist, type MusicLibraryResponse, type MusicLibrarySong } from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { playlistArtwork, songArtist, songArtwork, visiblePlatformIds } from './musicPresentation';
import { MusicPlaylistOutboundSync } from './MusicPlaylistOutboundSync';
import { MusicPlaylistRename } from './MusicPlaylistRename';
import { usePlaylistSyncDetails } from './usePlaylistSyncDetails';

function getPlaylistSongs(library: MusicLibraryResponse, playlistId: string) {
  return library.songs
    .flatMap((song) => song.playlists
      .filter((entry) => entry.playlistId === playlistId)
      .map((entry) => ({ song, position: entry.position, entryId: entry.entryId })))
    .sort((left, right) => left.position - right.position);
}

function mapSongToCollectionTrack(song: MusicLibrarySong, index: number, entryId: string): MusicCollectionTrack {
  return {
    id: song.id,
    entryId,
    detailSongId: song.id,
    title: song.title,
    artist: songArtist(song),
    albums: song.albums,
    artworkUrl: songArtwork(song, index),
    durationSeconds: song.durationSeconds,
    addedLabel: song.playlists[0]?.playlistName,
    platformIds: visiblePlatformIds(song),
  };
}

function getSuggestions(library: MusicLibraryResponse, currentPlaylistId: string): MusicCollectionSuggestion[] {
  return library.playlists
    .filter((playlist) => playlist.id !== currentPlaylistId)
    .slice(0, 5)
    .map((playlist, index) => ({
      id: playlist.id,
      title: playlist.name,
      detail: `${playlist.entryCount.toLocaleString()} songs`,
      artworkUrl: playlistArtwork(playlist, index),
      route: { type: 'libraryPlaylist' },
    }));
}

function PlaylistRenameAction({ playlist, sync }: { playlist: MusicLibraryPlaylist; sync: ReturnType<typeof usePlaylistSyncDetails> }) {
  const busy = !sync.details || sync.busy !== null;
  return <MusicPlaylistRename playlistId={playlist.id} currentName={sync.details?.name ?? playlist.name} busy={busy} onAction={sync.perform} />;
}

export function MusicPlaylistDetailPage({
  library,
  playlistId,
}: {
  library: MusicLibraryResponse;
  playlistId: string;
}) {
  const playlist = library.playlists.find((item) => item.id === playlistId);
  const sync = usePlaylistSyncDetails(playlistId);
  const playlistSongEntries = playlist ? getPlaylistSongs(library, playlistId) : [];
  const baseTracks = playlistSongEntries.map(({ song, entryId }, index) => mapSongToCollectionTrack(song, index, entryId));

  if (!playlist) {
    return (
      <MusicPageShell library={library}>
        <MusicEmptyPanel title="Playlist not found" detail="Cantaro could not find this playlist in your synced music library." />
      </MusicPageShell>
    );
  }

  return (
    <MusicPageShell library={library}>
      <MusicCollectionDetailPage
        title={playlist.name}
        description={playlist.description}
        artworkUrl={playlistArtwork(playlist)}
        backTo="/music/playlists"
        backLabel="Back to playlists"
        songsLabel={`${playlist.entryCount.toLocaleString()} songs`}
        tracks={baseTracks}
        emptyTrackLabel="This playlist has no songs yet"
        actionSlot={<PlaylistRenameAction playlist={playlist} sync={sync} />}
        afterHeroSlot={<MusicPlaylistOutboundSync state={sync} />}
        suggestions={getSuggestions(library, playlist.id)}
      />
    </MusicPageShell>
  );
}
