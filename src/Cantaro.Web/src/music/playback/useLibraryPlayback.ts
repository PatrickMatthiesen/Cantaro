import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { useMusicPlayback } from './musicPlaybackContextValue';
import { playbackTrackFromSong } from './playbackSources';
import { currentPlaybackTrack } from './playbackState';

export function useLibraryPlayback(songs: MusicLibrarySong[]) {
  const { state, dispatch } = useMusicPlayback();
  const upcomingIndex = (state.currentIndex ?? -1) + 1;

  function addTrack(track: { id: string }, play: boolean) {
    const song = songs.find((candidate) => candidate.id === track.id);
    if (song) dispatch({ type: 'enqueue', track: playbackTrackFromSong(song), play });
  }

  return {
    hasQueue: state.queue.length > 0,
    trackActions: {
      onPlayTrack: (track: { id: string }) => addTrack(track, true),
      onQueueTrack: (track: { id: string }) => addTrack(track, false),
    },
    queueProps: {
      activeTrack: currentPlaybackTrack(state) ?? undefined,
      queuedTracks: state.queue.slice(upcomingIndex),
      onClearQueue: () => dispatch({ type: 'clearQueue' }),
      onPlayQueuedTrack: (index: number) => dispatch({ type: 'select', index: upcomingIndex + index, play: true }),
    },
    playCollection: (collectionSongs: MusicLibrarySong[]) => dispatch({
      type: 'replaceQueue', tracks: collectionSongs.map((song) => playbackTrackFromSong(song)), play: true,
    }),
  };
}
