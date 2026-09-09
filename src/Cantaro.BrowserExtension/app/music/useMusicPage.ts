import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { useMemo, useState } from 'react';
import { useMusicLibraryState } from './useMusicLibraryState';

type MusicRoute = { kind: 'home' } | { kind: 'song'; song: MusicLibrarySong };

export function useMusicPage() {
  const [route, setRoute] = useState<MusicRoute>({ kind: 'home' });
  const [query, setQuery] = useState('');
  const libraryState = useMusicLibraryState();
  const results = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!libraryState.library) return [];
    return libraryState.library.songs.filter((song) => !term
      || `${song.title} ${song.artist ?? ''} ${song.albums.join(' ')}`.toLowerCase().includes(term)).slice(0, 40);
  }, [libraryState.library, query]);

  return {
    ...libraryState,
    route,
    setRoute,
    query,
    setQuery,
    results,
  };
}
