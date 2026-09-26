import { playlistSyncApi, type PlatformDisconnectPreview, type PlaylistSyncService } from '@cantaro/client-shared/music';
import { useCallback, useEffect, useRef, useState } from 'react';
import { platformName } from './musicPresentation';
import { requestPlaylistSyncDataRefresh } from './playlistSyncProgress';

type LinkChoice = { mappingId: string; deleteRemote: boolean; deleteCanonicalIfLast: boolean };
type DisconnectLink = PlatformDisconnectPreview['links'][number];

function useDisconnectReview(service: PlaylistSyncService, open: boolean, onDisconnected: () => void) {
  const [preview, setPreview] = useState<PlatformDisconnectPreview | null>(null);
  const [choices, setChoices] = useState<LinkChoice[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const generation = useRef(0);

  const load = useCallback(async () => {
    const current = ++generation.current;
    setLoading(true);
    setError(null);
    try {
      const loaded = await playlistSyncApi.previewDisconnect(service);
      if (current !== generation.current) return;
      setPreview(loaded);
      setChoices(loaded.links.map(({ mappingId }) => ({ mappingId, deleteRemote: false, deleteCanonicalIfLast: false })));
    } catch (loadError) {
      if (current === generation.current) setError(loadError instanceof Error ? loadError.message : 'Could not review linked playlists.');
    } finally {
      if (current === generation.current) setLoading(false);
    }
  }, [service]);

  useEffect(() => {
    if (open) void load();
    else { setPreview(null); setChoices([]); setError(null); }
    const generationRef = generation;
    return () => { generationRef.current++; };
  }, [open, load]);

  async function confirm() {
    if (!preview || loading) return;
    setLoading(true);
    setError(null);
    try {
      await playlistSyncApi.disconnect(service, choices);
      requestPlaylistSyncDataRefresh();
      onDisconnected();
    } catch (disconnectError) {
      setError(disconnectError instanceof Error ? disconnectError.message : `Could not disconnect ${platformName(service)}.`);
    } finally {
      setLoading(false);
    }
  }

  function update(mappingId: string, key: 'deleteRemote' | 'deleteCanonicalIfLast', value: boolean) {
    setChoices((current) => current.map((choice) => choice.mappingId === mappingId ? { ...choice, [key]: value } : choice));
  }

  return { preview, choices, loading, error, load, confirm, update };
}

function DisconnectChoiceRow({ service, link, choice, disabled, onChange }: {
  service: PlaylistSyncService;
  link: DisconnectLink;
  choice: LinkChoice | undefined;
  disabled: boolean;
  onChange: (mappingId: string, key: 'deleteRemote' | 'deleteCanonicalIfLast', value: boolean) => void;
}) {
  return <div className="border-t border-warning-border pt-3 first:border-t-0 first:pt-0">
    <p className="font-bold">{link.playlistName}</p>
    {link.canDeleteRemote ? <DisconnectChoiceToggle checked={Boolean(choice?.deleteRemote)} disabled={disabled} onChange={(value) => onChange(link.mappingId, 'deleteRemote', value)}>Delete the {platformName(service)} playlist</DisconnectChoiceToggle> : <p className="text-content-muted">The {platformName(service)} playlist will stay.</p>}
    {link.canDeleteCanonicalIfLast ? <DisconnectChoiceToggle checked={Boolean(choice?.deleteCanonicalIfLast)} disabled={disabled} onChange={(value) => onChange(link.mappingId, 'deleteCanonicalIfLast', value)}>Delete the Cantaro playlist too</DisconnectChoiceToggle> : null}
  </div>;
}

function DisconnectChoiceToggle({ checked, disabled, onChange, children }: {
  checked: boolean;
  disabled: boolean;
  onChange: (value: boolean) => void;
  children: React.ReactNode;
}) {
  return <label className="flex min-h-11 items-center gap-2"><input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />{children}</label>;
}

function DisconnectReviewContent({ service, state, onClose }: {
  service: PlaylistSyncService;
  state: ReturnType<typeof useDisconnectReview>;
  onClose: () => void;
}) {
  return <section role="region" aria-label={`Disconnect ${platformName(service)}`} className="space-y-3 border border-warning-border bg-warning-surface p-4 text-sm text-content">
    <h2 className="font-black">Disconnect {platformName(service)}</h2>
    {state.loading && !state.preview ? <p role="status">Loading linked playlists...</p> : null}
    {state.preview?.links.length === 0 ? <p>No Cantaro playlists are linked to this account.</p> : null}
    {state.preview?.links.map((link) => <DisconnectChoiceRow key={link.mappingId} service={service} link={link} choice={state.choices.find((item) => item.mappingId === link.mappingId)} disabled={state.loading} onChange={state.update} />)}
    {state.error ? <p role="alert" className="text-danger-content">{state.error} <button type="button" onClick={() => void state.load()} className="font-bold underline">Retry review</button></p> : null}
    <div className="flex flex-wrap gap-2">
      <button type="button" disabled={!state.preview || state.loading} onClick={() => void state.confirm()} className="min-h-11 bg-danger-action px-4 font-bold text-danger-action-content disabled:opacity-50">Disconnect account</button>
      <button type="button" disabled={state.loading} onClick={onClose} className="min-h-11 px-3 font-semibold underline">Cancel</button>
    </div>
  </section>;
}

export function MusicPlatformDisconnectReview({ service, open, onClose, onDisconnected }: {
  service: PlaylistSyncService;
  open: boolean;
  onClose: () => void;
  onDisconnected: () => void;
}) {
  const state = useDisconnectReview(service, open, onDisconnected);
  return open ? <DisconnectReviewContent service={service} state={state} onClose={onClose} /> : null;
}
