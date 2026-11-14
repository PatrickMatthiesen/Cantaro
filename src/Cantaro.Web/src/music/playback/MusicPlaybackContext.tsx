import { useReducer, type ReactNode } from 'react';
import { initialPlaybackState, playbackReducer } from './playbackState';
import { MusicPlaybackContext } from './musicPlaybackContextValue';

export function MusicPlaybackProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(playbackReducer, initialPlaybackState);
  return <MusicPlaybackContext.Provider value={{ state, dispatch }}>{children}</MusicPlaybackContext.Provider>;
}
