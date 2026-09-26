import { useState } from 'react';

export function usePlaylistSyncPreview<T>(fallbackError: string) {
  const [preview, setPreview] = useState<T | null>(null);
  const [preparing, setPreparing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function prepare(operation: () => Promise<T>) {
    setPreparing(true);
    setError(null);
    try { setPreview(await operation()); }
    catch (loadError) { setError(loadError instanceof Error ? loadError.message : fallbackError); }
    finally { setPreparing(false); }
  }

  return { preview, setPreview, preparing, error, prepare };
}
