import { createContext, useContext, type Dispatch } from 'react';
import type { PlaybackAction, PlaybackState } from './playbackState';

export const MusicPlaybackContext = createContext<{
  state: PlaybackState;
  dispatch: Dispatch<PlaybackAction>;
} | null>(null);

export function useMusicPlayback() {
  const context = useContext(MusicPlaybackContext);
  if (!context) throw new Error('useMusicPlayback requires MusicPlaybackProvider');
  return context;
}
