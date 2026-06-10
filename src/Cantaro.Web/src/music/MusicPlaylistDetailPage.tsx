import { useState } from 'react';
import { type MusicLibraryPlaylist, type MusicLibraryResponse, type MusicLibrarySong } from '@cantaro/client-shared/music';
import { MusicCollectionDetailPage, type MusicCollectionSuggestion, type MusicCollectionTrack } from './MusicCollectionDetailPage';
import { MusicEmptyPanel } from './MusicEmptyPanel';
import { MusicPageShell } from './MusicPageShell';
import { platformName, playlistArtwork, songArtist, songArtwork, visiblePlatformNames } from './musicPresentation';

function getPlaylistSongs(library: MusicLibraryResponse, playlistId: string) {
  return library.songs
    .map((song) => {
      const playlistEntry = song.playlists.find((entry) => entry.playlistId === playlistId);
      return playlistEntry ? { song, position: playlistEntry.position } : null;
    })
    .filter((entry): entry is { song: MusicLibrarySong; position: number } => Boolean(entry))
    .sort((left, right) => left.position - right.position);
}

function getLastSyncedAt(playlist: MusicLibraryPlaylist) {
  return playlist.services
    .map((service) => service.lastSyncedAt)
    .filter((value): value is string => Boolean(value))
    .sort()
    .at(-1);
}

function mapSongToCollectionTrack(song: MusicLibrarySong, index: number, activeSongId?: string): MusicCollectionTrack {
  const platforms = visiblePlatformNames(song);

  return {
    id: song.id,
    title: song.title,
    artist: songArtist(song),
    album: platforms.join(', '),
    artworkUrl: songArtwork(song, index),
    durationSeconds: song.durationSeconds,
    addedLabel: song.playlists[0]?.playlistName,
    platformNames: platforms,
    isPlaying: song.id === activeSongId,
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
    }));
}

export function MusicPlaylistDetailPage({
  library,
  playlistId,
}: {
  library: MusicLibraryResponse;
  playlistId: string;
}) {
  const [activeSongId, setActiveSongId] = useState<string | undefined>();
  const [queuedSongId, setQueuedSongId] = useState<string | undefined>();
  const playlist = library.playlists.find((item) => item.id === playlistId);

  if (!playlist) {
    return (
      <MusicPageShell library={library}>
        <MusicEmptyPanel title="Playlist not found" detail="Cantaro could not find this playlist in your synced music library." />
      </MusicPageShell>
    );
  }

  const playlistSongEntries = getPlaylistSongs(library, playlistId);
  const activeSong = playlistSongEntries.find(({ song }) => song.id === activeSongId)?.song;
  const tracks = playlistSongEntries.map(({ song }, index) => mapSongToCollectionTrack(song, index, activeSongId));
  const activeTrack = tracks.find((track) => track.id === activeSongId);
  const queuedTrack = tracks.find((track) => track.id === queuedSongId);
  const chips = playlist.services.length > 0 ? playlist.services.map((service) => platformName(service.service)) : ['Cantaro'];
  const trackIds = tracks.map((track) => track.id);

  const playTrack = (track: MusicCollectionTrack) => {
    setActiveSongId(track.id);
  };

  const queueTrack = (track: MusicCollectionTrack) => {
    setQueuedSongId(track.id);
  };

  const playFirstTrack = () => {
    setActiveSongId(trackIds[0]);
  };

  const playRandomTrack = () => {
    if (trackIds.length === 0) return;
    setActiveSongId(trackIds[Math.floor(Math.random() * trackIds.length)]);
  };

  return (
    <MusicPageShell library={library} activeSong={activeSong} onStopActiveSong={() => setActiveSongId(undefined)}>
      <MusicCollectionDetailPage
        eyebrow="Playlist"
        title={playlist.name}
        description={playlist.description}
        artworkUrl={playlistArtwork(playlist)}
        backTo="/music/playlists"
        backLabel="Back to playlists"
        ownerLabel="Created by you"
        updatedAt={getLastSyncedAt(playlist)}
        songsLabel={`${playlist.entryCount.toLocaleString()} songs`}
        chips={chips}
        tracks={tracks}
        emptyTrackLabel="This playlist has no songs yet"
        activeTrack={activeTrack}
        queuedTrack={queuedTrack}
        suggestions={getSuggestions(library, playlist.id)}
        onPlayAll={playFirstTrack}
        onShuffle={playRandomTrack}
        onPlayTrack={playTrack}
        onQueueTrack={queueTrack}
      />
    </MusicPageShell>
  );
}
