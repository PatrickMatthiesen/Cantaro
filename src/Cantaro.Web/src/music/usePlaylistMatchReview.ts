import { playlistSyncApi, type PlaylistMatchCandidate, type PlaylistMatchSearch } from '@cantaro/client-shared/music';
import { useCallback, useEffect, useRef, useState } from 'react';

export function usePlaylistMatchReview(playlistId: string, mappingId: string, entryId: string, onSync: () => Promise<boolean>, onResolved: () => void) {
  const [result, setResult] = useState<PlaylistMatchSearch | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [searching, setSearching] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [selected, setSelected] = useState<PlaylistMatchCandidate | null>(null);
  const generation = useRef(0);
  const locked = useRef(false);

  const search = useCallback(async (query?: string) => {
    const current = ++generation.current;
    setSearching(true);
    setError(null);
    setSelected(null);
    try {
      const response = await playlistSyncApi.searchMatch(playlistId, mappingId, entryId, query);
      if (current === generation.current) setResult(response);
    } catch (failure) {
      if (current === generation.current) setError(failure instanceof Error ? failure.message : 'Search failed. Try again.');
    } finally {
      if (current === generation.current) setSearching(false);
    }
  }, [playlistId, mappingId, entryId]);

  useEffect(() => {
    void search();
    const currentGeneration = generation;
    return () => { currentGeneration.current++; };
  }, [search]);

  async function saveMatch() {
    if (saved) return;
    if (!selected) throw new Error('Choose a recording first.');
    await playlistSyncApi.confirmMatch(playlistId, mappingId, entryId, selected.candidateToken);
    setSaved(true);
  }

  async function confirm() {
    if (locked.current) return;
    locked.current = true;
    setSaving(true);
    setError(null);
    try {
      await saveMatch();
      if (await onSync()) onResolved();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save this match. Try again.');
    } finally {
      locked.current = false;
      setSaving(false);
    }
  }

  return { result, error, searching, saving, saved, selected, setSelected, search, confirm };
}
