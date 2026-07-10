import { createContext, useContext } from 'react';
import type { MusicLibraryResponse } from '@cantaro/client-shared/music';

interface MusicLibraryState {
  library: MusicLibraryResponse | null;
  error: string | null;
  isLoading: boolean;
  reload: () => Promise<void>;
}

export const MusicLibraryStateContext = createContext<MusicLibraryState | null>(null);

export function useMusicLibraryContext() {
  const context = useContext(MusicLibraryStateContext);

  if (!context) {
    throw new Error('useMusicLibraryContext must be used within a MusicLibraryProvider');
  }

  return context;
}
