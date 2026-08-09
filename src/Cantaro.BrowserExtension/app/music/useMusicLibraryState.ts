import type { MusicLibraryResponse } from '@cantaro/client-shared/music';
import { useEffect, useState } from 'react';
import { loadMusicLibrary } from './musicService';

export function useMusicLibraryState() {
  const [library, setLibrary] = useState<MusicLibraryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setError(null);
    void loadMusicLibrary()
      .then((nextLibrary) => {
        if (active) setLibrary(nextLibrary);
      })
      .catch((reason: unknown) => {
        if (active) setError(reason instanceof Error ? reason.message : 'Could not load music.');
      });
    return () => { active = false; };
  }, []);

  return { library, error };
}
