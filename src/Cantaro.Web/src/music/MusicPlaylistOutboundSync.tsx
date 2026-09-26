import { Link } from '@tanstack/react-router';
import { MusicPlatformIcon, playlistSyncApi, type PlaylistCreateCandidate, type PlaylistSyncDetails, type PlaylistSyncService } from '@cantaro/client-shared/music';
import { useState, type RefObject } from 'react';
import { RefreshCw, Settings2 } from 'lucide-react';
import { MusicPlaylistAttachForm } from './MusicPlaylistAttach';
import { PlaylistCreatePanel, PlaylistCreateTrigger } from './MusicPlaylistCreateReview';
import { usePlaylistCreateReview } from './usePlaylistCreateReview';
import { MusicPlaylistLinkRow } from './MusicPlaylistLinkRow';
import { MusicRelativeTime } from './MusicRelativeTime';
import { platformName } from './musicPresentation';
import { useConnectedMusicPlatforms } from './useConnectedMusicPlatforms';
import { usePlaylistSyncDetails } from './usePlaylistSyncDetails';
import { useDismissiblePanel } from './useDismissiblePanel';

const services: PlaylistSyncService[] = ['youtube', 'spotify'];
type SyncState = ReturnType<typeof usePlaylistSyncDetails>;

function SettingSwitch({ label, checked, busy, onChange }: { label: string; checked: boolean; busy: boolean; onChange: () => void }) {
  return <button type="button" role="switch" aria-checked={checked} disabled={busy} onClick={onChange}
    className="flex min-h-11 w-full items-center justify-between gap-4 self-start text-left text-sm text-content focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">
    <span>{label}</span>
    <span aria-hidden className={`flex h-6 w-10 shrink-0 items-center rounded-full p-1 transition-colors ${checked ? 'bg-personal-accent' : 'bg-border-strong'}`}>
      <span className={`size-4 rounded-full transition-transform motion-reduce:transition-none ${checked ? 'translate-x-4 bg-personal-accent-content' : 'bg-content-muted'}`} />
    </span>
  </button>;
}

function PlaylistSyncSettings({ details, state, busy }: { details: PlaylistSyncDetails; state: SyncState; busy: boolean }) {
  return <div className="mb-2 grid gap-x-10 border-y border-border-subtle py-3 sm:grid-cols-2">
    <div>
      <SettingSwitch label="Daily sync" checked={details.syncEnabled} busy={busy} onChange={() => void state.perform('daily-sync', () => playlistSyncApi.setEnabled(details.playlistId, !details.syncEnabled))} />
      {details.syncEnabled && details.nextSyncAt ? <p className="text-xs text-content-muted">Next sync <MusicRelativeTime value={details.nextSyncAt} /></p> : null}
    </div>
    <SettingSwitch label="Allow duplicate tracks" checked={details.allowDuplicateTracks} busy={busy} onChange={() => void state.perform('duplicates', () => playlistSyncApi.setDuplicates(details.playlistId, !details.allowDuplicateTracks))} />
  </div>;
}

function UnlinkedAttachPanel({ open, panelRef, panelId, playlistId, playlistName, service, busy, preselected, state, onClose }: {
  open: boolean; panelRef: RefObject<HTMLDivElement | null>; panelId: string;
  playlistId: string; playlistName: string; service: PlaylistSyncService; busy: boolean;
  preselected: PlaylistCreateCandidate | undefined; state: SyncState; onClose: () => void;
}) {
  if (!open) return null;
  return <div ref={panelRef} id={panelId} className="pt-3"><MusicPlaylistAttachForm key={preselected?.servicePlaylistId ?? 'manual'} playlistName={playlistName} playlistId={playlistId} service={service} busy={busy} preselected={preselected} onAttached={(operation) => state.perform('attach', operation)} onClose={onClose} /></div>;
}

function UnlinkedPlatformRow({ playlistId, playlistName, service, state, busy }: { playlistId: string; playlistName: string; service: PlaylistSyncService; state: SyncState; busy: boolean }) {
  const { open: attaching, setOpen: setAttaching, triggerRef, panelRef, panelId } = useDismissiblePanel();
  const [preselected, setPreselected] = useState<PlaylistCreateCandidate | undefined>();
  const { open: createOpen, setOpen: setCreateOpen, triggerRef: createTriggerRef, panelRef: createPanelRef, panelId: createPanelId } = useDismissiblePanel();
  const create = usePlaylistCreateReview(playlistId, service, (operation) => state.perform('create-link', operation), createOpen, setCreateOpen);
  function selectCandidate(candidate: PlaylistCreateCandidate) {
    create.close();
    setPreselected(candidate);
    setAttaching(true);
  }
  return <div className="border-t border-border-subtle py-4">
    <div className="grid items-center gap-3 sm:grid-cols-[minmax(0,1fr)_15rem]">
      <div className="flex items-center gap-3"><MusicPlatformIcon platformId={service} className="size-6 shrink-0" /><span className="text-sm font-semibold text-content">{platformName(service)}</span></div>
      <div className="grid grid-cols-[minmax(0,1fr)_6rem] items-center gap-2 max-sm:ml-9">
        <PlaylistCreateTrigger label="Create playlist" state={create} open={createOpen} busy={busy} triggerRef={createTriggerRef} panelId={createPanelId} onStart={() => setAttaching(false)} />
        <button type="button" disabled={busy || create.checking || create.creating} ref={triggerRef} onClick={() => { create.close(); setPreselected(undefined); setAttaching(!attaching); }} aria-expanded={attaching} aria-controls={panelId} className="min-h-11 px-1 text-sm font-medium whitespace-nowrap text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">Link existing</button>
      </div>
    </div>
    <PlaylistCreatePanel open={createOpen} panelRef={createPanelRef} panelId={createPanelId} service={service} state={create} onSelect={selectCandidate} />
    <UnlinkedAttachPanel open={attaching} panelRef={panelRef} panelId={panelId} playlistId={playlistId} playlistName={playlistName} service={service} busy={busy} preselected={preselected} state={state} onClose={() => { setAttaching(false); triggerRef.current?.focus(); }} />
  </div>;
}

function SyncHeader({ state, open, setOpen, triggerRef, panelId }: { state: SyncState } & Pick<ReturnType<typeof useDismissiblePanel>, 'open' | 'setOpen' | 'triggerRef' | 'panelId'>) {
  return <div className="mb-1 flex items-center justify-between gap-3">
      <h2 id="playlist-sync-heading" className="text-base font-bold text-content">Playlist sync</h2>
      {state.details ? <div className="flex items-center gap-1">
        <button type="button" disabled={state.busy !== null || state.isLoading} onClick={() => void state.reload()} aria-label="Refresh sync status" title="Refresh sync status" className="flex size-11 items-center justify-center text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50"><RefreshCw className={`size-4 ${state.isLoading ? 'motion-safe:animate-spin' : ''}`} aria-hidden /></button>
        <button type="button" ref={triggerRef} onClick={() => setOpen(!open)} aria-expanded={open} aria-controls={panelId} className="inline-flex min-h-11 items-center gap-2 px-3 text-sm font-medium text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus"><Settings2 className="size-4" aria-hidden />Settings</button>
      </div> : null}
    </div>;
}

function PlatformLinks({ details, state, connected, busy }: { details: PlaylistSyncDetails; state: SyncState; connected: PlaylistSyncService[]; busy: boolean }) {
  const unlinked = connected.filter((service) => !details.links.some((link) => link.service === service));
  return <>
    {details.links.map((link) => <MusicPlaylistLinkRow key={link.mappingId} playlistId={details.playlistId} playlistName={details.name} link={link} linkCount={details.links.length} busy={busy} syncing={state.busy === `sync-${link.mappingId}`} onAction={state.perform} />)}
    {unlinked.map((service) => <UnlinkedPlatformRow key={service} playlistId={details.playlistId} playlistName={details.name} service={service} state={state} busy={busy} />)}
    {connected.length === 0 ? <Link to="/music/platforms" className="inline-flex min-h-11 items-center text-sm font-semibold text-content underline">Connect a platform</Link> : null}
  </>;
}

function SyncFeedback({ state, isCheckingConnectedAccounts }: { state: SyncState; isCheckingConnectedAccounts: boolean }) {
  const details = state.details;
  return <>
    {(state.isLoading || isCheckingConnectedAccounts) && !details ? <p role="status" className="py-3 text-sm text-content-muted">Loading playlist links...</p> : null}
    {state.error ? <div role="alert" className="py-2 text-sm text-danger-content">{state.error} <button type="button" onClick={() => void state.reload()} className="font-bold underline">Retry</button></div> : null}
  </>;
}

export function MusicPlaylistOutboundSync({ state }: { state: SyncState }) {
  const { connectedPlatformIds, isCheckingConnectedAccounts } = useConnectedMusicPlatforms();
  const { open, setOpen, triggerRef, panelRef, panelId } = useDismissiblePanel();
  const details = state.details;
  const busy = state.busy !== null;
  const connected = services.filter((service) => connectedPlatformIds.includes(service));
  return <section aria-labelledby="playlist-sync-heading">
    <SyncHeader state={state} open={open} setOpen={setOpen} triggerRef={triggerRef} panelId={panelId} />
    <SyncFeedback state={state} isCheckingConnectedAccounts={isCheckingConnectedAccounts} />
    {details ? <>
      {open ? <div ref={panelRef} id={panelId}><PlaylistSyncSettings details={details} state={state} busy={busy} /></div> : null}
      <PlatformLinks details={details} state={state} connected={connected} busy={busy} />
    </> : null}
  </section>;
}
