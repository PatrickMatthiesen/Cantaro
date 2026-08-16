import { type MusicLibraryResponse, type MusicLibrarySong } from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { platformName, playlistArtwork, playlistLastSyncedAt, songArtist, songArtwork, visiblePlatformIds } from './musicPresentation';

function getPlaylistSongs(library: MusicLibraryResponse, playlistId: string) {
  return library.songs
    .map((song) => {
      const playlistEntry = song.playlists.find((entry) => entry.playlistId === playlistId);
      return playlistEntry ? { song, position: playlistEntry.position } : null;
    })
    .filter((entry): entry is { song: MusicLibrarySong; position: number } => Boolean(entry))
    .sort((left, right) => left.position - right.position);
}

function mapSongToCollectionTrack(song: MusicLibrarySong, index: number): MusicCollectionTrack {
  return {
    id: song.id,
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

export function MusicPlaylistDetailPage({
  library,
  playlistId,
}: {
  library: MusicLibraryResponse;
  playlistId: string;
}) {
  const playlist = library.playlists.find((item) => item.id === playlistId);
  const playlistSongEntries = playlist ? getPlaylistSongs(library, playlistId) : [];
  const baseTracks = playlistSongEntries.map(({ song }, index) => mapSongToCollectionTrack(song, index));

  if (!playlist) {
    return (
      <MusicPageShell library={library}>
        <MusicEmptyPanel title="Playlist not found" detail="Cantaro could not find this playlist in your synced music library." />
      </MusicPageShell>
    );
  }

  const chips = playlist.services.length > 0 ? playlist.services.map((service) => platformName(service.service)) : ['Cantaro'];

  return (
    <MusicPageShell library={library}>
      <MusicCollectionDetailPage
        eyebrow="Playlist"
        title={playlist.name}
        description={playlist.description}
        artworkUrl={playlistArtwork(playlist)}
        backTo="/music/playlists"
        backLabel="Back to playlists"
        ownerLabel="Created by you"
        updatedAt={playlistLastSyncedAt(playlist) ?? undefined}
        songsLabel={`${playlist.entryCount.toLocaleString()} songs`}
        chips={chips}
        tracks={baseTracks}
        emptyTrackLabel="This playlist has no songs yet"
        suggestions={getSuggestions(library, playlist.id)}
      />
    </MusicPageShell>
  );
}
