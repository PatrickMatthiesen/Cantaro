import { playlistSyncApi, type PlaylistAttachPreview, type PlaylistSyncDetails, type PlaylistSyncLink } from '@cantaro/client-shared/music';
import { usePlaylistSyncPreview } from './usePlaylistSyncPreview';

function RetryActions({ preview, preparing, busy, onConfirm, onPrepare, onCancel }: {
  preview: PlaylistAttachPreview | null;
  preparing: boolean;
  busy: boolean;
  onConfirm: () => void;
  onPrepare: () => void;
  onCancel: () => void;
}) {
  return <div className="flex flex-wrap gap-2">
    {preview ? <button type="button" disabled={busy} onClick={onConfirm} className="min-h-11 border border-warning-border px-3 font-bold disabled:opacity-50">Retry first sync</button>
      : <button type="button" disabled={busy || preparing} onClick={onPrepare} className="min-h-11 border border-warning-border px-3 font-bold disabled:opacity-50">{preparing ? 'Refreshing...' : 'Review retry'}</button>}
    {preview ? <button type="button" disabled={busy} onClick={onCancel} className="min-h-11 px-2 underline">Cancel</button> : null}
  </div>;
}

function PendingInitializationRetry({ playlistId, link, busy, onAction }: {
  playlistId: string;
  link: PlaylistSyncLink;
  busy: boolean;
  onAction: (label: string, operation: () => Promise<PlaylistSyncDetails>) => Promise<boolean>;
}) {
  const { preview, setPreview, preparing, error, prepare } = usePlaylistSyncPreview<PlaylistAttachPreview>('Could not refresh the first sync preview.');
  const confirm = async () => {
    if (!preview) return;
    const succeeded = await onAction('initialization', () => playlistSyncApi.confirmInitialization(playlistId, link.mappingId, preview.previewToken));
    if (succeeded) setPreview(null);
  };
  return <div className="mt-2 space-y-2 text-sm text-warning-content">
    <p>First sync did not finish.</p>
    {preview ? <p>{preview.additions} provider-only tracks; {preview.removals} Cantaro-only tracks. The saved initial choices will be reused.</p> : null}
    {error ? <p role="alert" className="text-danger-content">{error}</p> : null}
    <RetryActions preview={preview} preparing={preparing} busy={busy} onConfirm={() => void confirm()} onPrepare={() => void prepare(() => playlistSyncApi.previewInitialization(playlistId, link.mappingId))} onCancel={() => setPreview(null)} />
  </div>;
}

export function MusicPlaylistInitializationRetry(props: Parameters<typeof PendingInitializationRetry>[0]) {
  return props.link.state === 'paused' && props.link.lastSyncStatus === 'initialization_pending'
    ? <PendingInitializationRetry {...props} /> : null;
}
