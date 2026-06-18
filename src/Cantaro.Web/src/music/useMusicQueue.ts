import { useEffect, useState } from 'react';

export interface MusicQueueTrack {
  id: string;
  title: string;
  artist?: string;
  artworkUrl?: string;
  durationSeconds?: number;
}

function shuffleTracks<T>(tracks: T[]): T[] {
  const shuffled = [...tracks];

  for (let index = shuffled.length - 1; index > 0; index -= 1) {
    const swapIndex = Math.floor(Math.random() * (index + 1));
    [shuffled[index], shuffled[swapIndex]] = [shuffled[swapIndex], shuffled[index]];
  }

  return shuffled;
}

export function useMusicQueue<T extends MusicQueueTrack>(tracks: T[], resetKey?: string) {
  const [activeTrackId, setActiveTrackId] = useState<string | undefined>();
  const [queuedTrackIds, setQueuedTrackIds] = useState<string[]>([]);

  useEffect(() => {
    setActiveTrackId(undefined);
    setQueuedTrackIds([]);
  }, [resetKey]);

  const activeTrack = tracks.find((track) => track.id === activeTrackId);
  const queuedTracks = queuedTrackIds
    .map((trackId) => tracks.find((track) => track.id === trackId))
    .filter((track): track is T => Boolean(track));

  const playTrack = (track: T) => {
    setActiveTrackId(track.id);
    setQueuedTrackIds((current) => current.filter((trackId) => trackId !== track.id));
  };

  const queueTrack = (track: T) => {
    if (track.id === activeTrackId) return;

    setQueuedTrackIds((current) => current.includes(track.id) ? current : [...current, track.id]);
  };

  const playAll = () => {
    const [firstTrack, ...remainingTracks] = tracks;
    setActiveTrackId(firstTrack?.id);
    setQueuedTrackIds(remainingTracks.map((track) => track.id));
  };

  const shuffle = () => {
    const [firstTrack, ...remainingTracks] = shuffleTracks(tracks);
    setActiveTrackId(firstTrack?.id);
    setQueuedTrackIds(remainingTracks.map((track) => track.id));
  };

  return {
    activeTrack,
    activeTrackId,
    queuedTracks,
    playTrack,
    queueTrack,
    playAll,
    shuffle,
    clearQueue: () => setQueuedTrackIds([]),
    stop: () => setActiveTrackId(undefined),
  };
}
