import type { MusicLibrarySong } from '@cantaro/client-shared/music';
import { useMemo, useState } from 'react';
import type { MusicTabContext } from '../../features/music/contracts/musicTabContext';
import type { ActiveTabContextState } from '../shell/extensionAppTypes';
import { resolveMusicTab } from './musicMatching';
import { useMusicLibraryState } from './useMusicLibraryState';
import { useMusicRecognition } from './useMusicRecognition';

type MusicRoute = { kind: 'home' } | { kind: 'song'; song: MusicLibrarySong };

function activeMusicContext(state: ActiveTabContextState): MusicTabContext | null {
  if (state.status !== 'available' || state.snapshot.feature !== 'music') return null;
  return state.snapshot;
}

export function useMusicPage(activeTabContext: ActiveTabContextState) {
  const [route, setRoute] = useState<MusicRoute>({ kind: 'home' });
  const [query, setQuery] = useState('');
  const libraryState = useMusicLibraryState();
  const context = activeMusicContext(activeTabContext);
  const recognition = useMusicRecognition(context);
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
    context,
    ...recognition,
    results,
    resolution: context && libraryState.library
      ? resolveMusicTab(context, libraryState.library.songs)
      : null,
  };
}
