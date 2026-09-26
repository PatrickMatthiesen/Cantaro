import { playlistSyncApi, type PlaylistRenamePreview, type PlaylistSyncDetails } from '@cantaro/client-shared/music';
import { useState } from 'react';
import { Pencil } from 'lucide-react';
import { platformName } from './musicPresentation';
import { usePlaylistSyncPreview } from './usePlaylistSyncPreview';

type RenameAction = (label: string, operation: () => Promise<PlaylistSyncDetails>) => Promise<boolean>;

function RenameEditor({ name, currentName, busy, preparing, onChange, onPrepare, onCancel }: {
  name: string;
  currentName: string;
  busy: boolean;
  preparing: boolean;
  onChange: (name: string) => void;
  onPrepare: () => void;
  onCancel: () => void;
}) {
  return <>
    <label className="block text-sm font-semibold text-content" htmlFor="cantaro-playlist-rename">New name</label>
    <input id="cantaro-playlist-rename" value={name} disabled={busy} onChange={(event) => onChange(event.target.value)} maxLength={200} className="min-h-11 w-full border border-border-strong bg-surface px-3 text-sm text-content focus-visible:outline-2 focus-visible:outline-focus" />
    <div className="flex flex-wrap gap-2">
      <button type="button" disabled={busy || preparing || !name.trim() || name.trim() === currentName} onClick={onPrepare} className="min-h-11 bg-action px-4 text-sm font-bold text-action-content disabled:opacity-50">{preparing ? 'Refreshing links...' : 'Review rename'}</button>
      <button type="button" disabled={busy} onClick={onCancel} className="min-h-11 px-3 text-sm font-semibold underline">Cancel</button>
    </div>
  </>;
}

function RenameReview({ preview, busy, onConfirm, onChange }: {
  preview: PlaylistRenamePreview;
  busy: boolean;
  onConfirm: () => void;
  onChange: () => void;
}) {
  return <>
    <div role="status" className="space-y-1 border border-warning-border bg-warning-surface p-3 text-sm text-content">
      <p className="font-bold">{preview.currentName} → {preview.proposedName}</p>
      {preview.links.map((link) => <p key={link.mappingId}>{platformName(link.service)}: {link.currentName}{link.willRename ? ` → ${preview.proposedName}` : ' (name stays)'}</p>)}
    </div>
    <div className="flex flex-wrap gap-2">
      <button type="button" disabled={busy} onClick={onConfirm} className="min-h-11 bg-action px-4 text-sm font-bold text-action-content disabled:opacity-50">Confirm rename</button>
      <button type="button" disabled={busy} onClick={onChange} className="min-h-11 px-3 text-sm font-semibold underline">Change name</button>
    </div>
  </>;
}

function OpenRenameForm({ playlistId, currentName, busy, onAction, onClose }: {
  playlistId: string;
  currentName: string;
  busy: boolean;
  onAction: RenameAction;
  onClose: () => void;
}) {
  const [name, setName] = useState(currentName);
  const { preview, setPreview, preparing, error, prepare } = usePlaylistSyncPreview<PlaylistRenamePreview>('Could not prepare the rename.');

  async function confirm() {
    if (!preview) return;
    const succeeded = await onAction('rename', () => playlistSyncApi.confirmRename(playlistId, preview.proposedName, preview.previewToken));
    if (succeeded) onClose();
  }

  return <div className="space-y-3 border-t border-border-subtle pt-3">
    {preview ? <RenameReview preview={preview} busy={busy} onConfirm={() => void confirm()} onChange={() => setPreview(null)} />
      : <RenameEditor name={name} currentName={currentName} busy={busy} preparing={preparing} onChange={setName} onPrepare={() => void prepare(() => playlistSyncApi.previewRename(playlistId, name.trim()))} onCancel={onClose} />}
    {error ? <p role="alert" className="text-sm text-danger-content">{error}</p> : null}
  </div>;
}

export function MusicPlaylistRename({ playlistId, currentName, busy, onAction }: {
  playlistId: string;
  currentName: string;
  busy: boolean;
  onAction: RenameAction;
}) {
  const [open, setOpen] = useState(false);
  return open ? <OpenRenameForm key={currentName} playlistId={playlistId} currentName={currentName} busy={busy} onAction={onAction} onClose={() => setOpen(false)} />
    : <button type="button" disabled={busy} onClick={() => setOpen(true)} className="inline-flex min-h-11 items-center gap-2 px-3 text-sm font-medium text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50"><Pencil className="size-4" aria-hidden />Rename</button>;
}
