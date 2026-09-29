import { playlistSyncApi, type PlaylistNameProposalPreview, type PlaylistSyncDetails, type PlaylistSyncLink } from '@cantaro/client-shared/music';
import { platformName } from './musicPresentation';
import { usePlaylistSyncPreview } from './usePlaylistSyncPreview';

function PendingNameReview({ playlistId, link, busy, onAction }: {
  playlistId: string;
  link: PlaylistSyncLink;
  busy: boolean;
  onAction: (label: string, operation: () => Promise<PlaylistSyncDetails>) => Promise<boolean>;
}) {
  const { preview, setPreview, preparing, error, prepare } = usePlaylistSyncPreview<PlaylistNameProposalPreview>('Could not refresh this name.');

  async function decide(accept: boolean) {
    if (!preview) return;
    const succeeded = await onAction('name-proposal', () => playlistSyncApi.decideNameProposal(playlistId, link.mappingId, accept, preview.previewToken));
    if (succeeded) setPreview(null);
  }

  return <div className="mt-2 space-y-2 text-sm">
    {preview ? <>
      <p className="text-content">{platformName(link.service)} name: {preview.providerName}. Cantaro name: {preview.currentName}.</p>
      <div className="flex flex-wrap gap-2">
      <button type="button" disabled={busy} onClick={() => void decide(true)} className="min-h-11 border border-border-strong bg-surface px-3 font-bold text-content disabled:opacity-50">Use provider name</button>
      <button type="button" disabled={busy} onClick={() => void decide(false)} className="min-h-11 border border-border-strong bg-surface px-3 font-bold text-content disabled:opacity-50">Keep Cantaro name</button>
      <button type="button" disabled={busy} onClick={() => setPreview(null)} className="min-h-11 px-2 underline">Cancel</button>
      </div>
    </> : <button type="button" disabled={busy || preparing} onClick={() => void prepare(() => playlistSyncApi.previewNameProposal(playlistId, link.mappingId))} className="min-h-11 text-warning-content underline disabled:opacity-50">{preparing ? 'Refreshing name...' : `Review ${platformName(link.service)} name`}</button>}
    {error ? <p role="alert" className="text-danger-content">{error}</p> : null}
  </div>;
}

export function MusicPlaylistNameProposal(props: Parameters<typeof PendingNameReview>[0]) {
  return props.link.pendingName ? <PendingNameReview {...props} /> : null;
}
