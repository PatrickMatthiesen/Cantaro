import { playlistSyncApi, type PlaylistCreatePreview, type PlaylistSyncDetails, type PlaylistSyncService } from '@cantaro/client-shared/music';
import { useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { PlaylistCreateRequestGate, previewAndCreatePlaylist } from './playlistCreateRequest';

type CreateAction = (operation: () => Promise<PlaylistSyncDetails>) => Promise<boolean>;

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Could not check playlists.';
}

export function usePlaylistCreateReview(
  playlistId: string, service: PlaylistSyncService, onCreated: CreateAction,
  open: boolean, setOpen: Dispatch<SetStateAction<boolean>>,
) {
  const [preview, setPreview] = useState<PlaylistCreatePreview | null>(null);
  const [checking, setChecking] = useState(false);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const gate = useRef(new PlaylistCreateRequestGate());

  function close() {
    gate.current.cancel();
    setChecking(false);
    setCreating(false);
    setOpen(false);
  }

  useEffect(() => {
    if (open) return;
    gate.current.cancel();
    setChecking(false);
    setCreating(false);
  }, [open]);

  useEffect(() => {
    const requests = gate.current;
    return () => { requests.cancel(); };
  }, []);

  async function runRequest(request: number, action: () => Promise<void>) {
    try {
      await action();
    } catch (requestError) {
      if (gate.current.isCurrent(request)) setError(errorMessage(requestError));
    } finally {
      if (gate.current.isCurrent(request)) {
        gate.current.cancel();
        setChecking(false);
        setCreating(false);
      }
    }
  }

  async function start() {
    const current = gate.current.begin();
    if (current === null) return;
    setOpen(true);
    setPreview(null);
    setError(null);
    setChecking(true);
    await runRequest(current, async () => {
      const result = await previewAndCreatePlaylist(
        gate.current,
        current,
        () => playlistSyncApi.previewCreateLink(playlistId, service),
        (token) => onCreated(() => playlistSyncApi.createLink(playlistId, service, token)),
        () => { setChecking(false); setCreating(true); },
      );
      if (!gate.current.isCurrent(current)) return;
      setChecking(false);
      if (result.kind === 'candidates') setPreview(result.preview);
      if (result.kind === 'created') close();
    });
  }

  async function createReviewed() {
    if (!preview) return;
    const current = gate.current.begin();
    if (current === null) return;
    setCreating(true);
    setError(null);
    await runRequest(current, async () => {
      await onCreated(() => playlistSyncApi.createLink(playlistId, service, preview.previewToken));
      if (gate.current.isCurrent(current)) close();
    });
  }

  return { preview, checking, creating, error, close, start, createReviewed };
}

export type PlaylistCreateReviewState = ReturnType<typeof usePlaylistCreateReview>;
