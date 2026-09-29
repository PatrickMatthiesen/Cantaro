import { Link } from '@tanstack/react-router';
import { Info } from 'lucide-react';
import {
  platformManager, playlistSyncApi, syncApi,
  type PlaylistAttachPreview, type PlaylistCreateCandidate, type PlaylistInitialMode,
  type PlaylistSyncDetails, type PlaylistSyncMode, type PlaylistSyncService,
} from '@cantaro/client-shared/music';
import { useEffect, useId, useState } from 'react';
import { platformName } from './musicPresentation';
import { playlistAttachCandidates, type PlaylistAttachCandidate as Candidate } from './playlistAttachCandidates';
import { useDismissiblePanel } from './useDismissiblePanel';

type AttachAction = (operation: () => Promise<PlaylistSyncDetails>) => Promise<boolean>;

function message(error: unknown) {
  return error instanceof Error ? error.message : 'Could not load playlists.';
}

function useAttachCandidates(service: PlaylistSyncService, preselected?: PlaylistCreateCandidate) {
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    void Promise.all([platformManager.playlists(service), syncApi.getSyncStatus()]).then(([playlists, status]) => {
      if (!active) return;
      const owners = new Map(status.playlists
        .filter((item) => item.service === service)
        .map((item) => [item.servicePlaylistId, item.playlistId]));
      const items = playlists.map((playlist) => ({ playlist, ownerId: owners.get(playlist.id) ?? null }));
      if (preselected && !items.some(({ playlist }) => playlist.id === preselected.servicePlaylistId)) {
        items.unshift({ playlist: {
          id: preselected.servicePlaylistId, title: preselected.name,
          itemCount: preselected.remoteTrackCount, songs: async () => [],
        }, ownerId: owners.get(preselected.servicePlaylistId) ?? null });
      }
      setCandidates(items);
    }).catch((loadError) => {
      if (!active) return;
      if (preselected) {
        setCandidates([{ playlist: {
          id: preselected.servicePlaylistId, title: preselected.name,
          itemCount: preselected.remoteTrackCount, songs: async () => [],
        }, ownerId: null }]);
      } else setError(message(loadError));
    })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [service, preselected]);
  return { candidates, loading, error };
}

function useAttachChoices(playlistId: string, service: PlaylistSyncService, onAttached: AttachAction, onClose: () => void, preselectedId?: string) {
  const [selectedId, setSelectedId] = useState(preselectedId ?? '');
  const [mode, setMode] = useState<PlaylistSyncMode>('bidirectional');
  const [initialMode, setInitialMode] = useState<PlaylistInitialMode>('combine');
  const [preview, setPreview] = useState<PlaylistAttachPreview | null>(null);
  const [reviewing, setReviewing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function review() {
    if (!selectedId || reviewing) return;
    setReviewing(true);
    setError(null);
    try { setPreview(await playlistSyncApi.previewAttach(playlistId, service, selectedId)); }
    catch (previewError) { setError(message(previewError)); }
    finally { setReviewing(false); }
  }

  async function attach() {
    if (!preview) return;
    const succeeded = await onAttached(() => playlistSyncApi.attach(playlistId, {
      service, servicePlaylistId: preview.servicePlaylistId, syncMode: mode, initialMode, previewToken: preview.previewToken,
    }));
    if (succeeded) onClose();
  }

  function select(id: string) { setSelectedId(id); setPreview(null); setError(null); }
  return { selectedId, setSelectedId: select, mode, setMode, initialMode, setInitialMode, preview, setPreview, reviewing, error, review, attach };
}

type Choices = ReturnType<typeof useAttachChoices>;

function ExistingPlaylistPicker({ service, playlistName, candidates, selectedId, disabled, onChange }: {
  service: PlaylistSyncService;
  playlistName: string;
  candidates: Candidate[];
  selectedId: string;
  disabled: boolean;
  onChange: (id: string) => void;
}) {
  const [query, setQuery] = useState('');
  const fieldId = useId();
  const items = playlistAttachCandidates(candidates, playlistName, query);
  return <fieldset disabled={disabled} className="min-w-0 space-y-2">
    <legend className="mb-2 text-sm font-semibold text-content">Existing {platformName(service)} playlist</legend>
    <input id={fieldId} type="search" aria-label={`Search ${platformName(service)} playlists`} placeholder="Search playlists" value={query} onChange={(event) => { setQuery(event.target.value); onChange(''); }} className="min-h-11 w-full border border-border-strong bg-surface px-3 text-sm text-content focus-visible:outline-2 focus-visible:outline-focus" />
    <div className="max-h-60 overflow-y-auto overscroll-contain border border-border-subtle">
      {items.map(({ playlist, ownerId, sameName }) => <div key={playlist.id} className="flex items-center gap-3 border-b border-border-subtle px-3 last:border-0">
        <label className={`flex min-h-11 min-w-0 flex-1 items-center gap-3 py-2 ${ownerId ? 'text-content-muted' : 'cursor-pointer text-content hover:bg-surface-hover'} ${selectedId === playlist.id ? 'bg-surface-hover' : ''}`}>
          <input type="radio" name={fieldId} value={playlist.id} checked={selectedId === playlist.id} disabled={!!ownerId} onChange={() => onChange(playlist.id)} className="size-4 shrink-0 accent-personal-accent focus-visible:outline-2 focus-visible:outline-focus" />
          <span className="min-w-0 flex-1 text-sm wrap-anywhere">{playlist.title}</span>
          {sameName ? <span className="shrink-0 text-xs font-semibold text-personal-accent">Same name</span> : null}
          <span className="shrink-0 text-xs text-content-muted">{playlist.itemCount} {playlist.itemCount === 1 ? 'track' : 'tracks'}</span>
        </label>
        {ownerId ? <Link to="/music/playlists/$playlistId" params={{ playlistId: ownerId }} className="inline-flex min-h-11 shrink-0 items-center text-xs text-content-muted underline focus-visible:outline-2 focus-visible:outline-focus">Already linked</Link> : null}
      </div>)}
      {items.length === 0 ? <p role="status" className="p-3 text-sm text-content-muted">{query ? 'No playlists match your search.' : 'No playlists available.'}</p> : null}
    </div>
  </fieldset>;
}

function AttachChoicesFields({ service, playlistName, choices, candidates, disabled }: {
  service: PlaylistSyncService;
  playlistName: string;
  choices: Choices;
  candidates: Candidate[];
  disabled: boolean;
}) {
  return <>
    <ExistingPlaylistPicker service={service} playlistName={playlistName} candidates={candidates} selectedId={choices.selectedId} disabled={disabled} onChange={choices.setSelectedId} />
    {choices.selectedId ? <div className="grid gap-3 sm:grid-cols-2">
      <label className="text-sm font-semibold text-content">Ongoing direction
        <select value={choices.mode} disabled={disabled} onChange={(event) => choices.setMode(event.target.value as PlaylistSyncMode)} className="mt-1 block min-h-11 w-full border border-border-strong bg-surface px-3 text-sm text-content focus-visible:outline-2 focus-visible:outline-focus">
          <option value="bidirectional">Two-way</option>
          <option value="import_only">{platformName(service)} to Cantaro</option>
          <option value="from_cantaro">Cantaro to {platformName(service)}</option>
        </select>
      </label>
      <label className="text-sm font-semibold text-content">Initial contents
        <select value={choices.initialMode} disabled={disabled} onChange={(event) => choices.setInitialMode(event.target.value as PlaylistInitialMode)} className="mt-1 block min-h-11 w-full border border-border-strong bg-surface px-3 text-sm text-content focus-visible:outline-2 focus-visible:outline-focus">
          <option value="combine">Combine both</option>
          <option value="platform">Start from {platformName(service)}</option>
          <option value="cantaro">Start from Cantaro</option>
        </select>
      </label>
    </div> : null}
  </>;
}

function UnavailableEntriesInfo({ count, service }: { count: number; service: PlaylistSyncService }) {
  const { open, setOpen, triggerRef, panelRef, panelId } = useDismissiblePanel();
  return <div className="relative shrink-0" onKeyDown={(event) => {
    if (event.key !== 'Escape' || !open) return;
    event.stopPropagation();
    setOpen(false);
    triggerRef.current?.focus();
  }}>
    <button type="button" ref={triggerRef} aria-label="Unavailable entries" aria-expanded={open} aria-controls={panelId}
      onClick={() => setOpen(!open)} className="flex size-11 items-center justify-center text-content-muted hover:text-content focus-visible:outline-2 focus-visible:outline-focus">
      <Info aria-hidden size={18} />
    </button>
    {open ? <div ref={panelRef} id={panelId} role="note" className="absolute right-0 top-full z-20 w-64 max-w-[calc(100vw-4rem)] border border-border-strong bg-surface-raised p-3 text-sm text-content">
      {count} unavailable {count === 1 ? 'entry stays' : 'entries stay'} on {platformName(service)}. The rest can sync.
    </div> : null}
  </div>;
}

function InitialPreview({ preview, mode }: { preview: PlaylistAttachPreview; mode: PlaylistInitialMode }) {
  const platform = platformName(preview.service);
  const changes = [
    preview.additions ? `${platform}: ${preview.additions} new` : null,
    preview.removals ? `Cantaro: ${preview.removals} new` : null,
  ].filter(Boolean).join(' · ') || 'No new tracks';
  const copy: Record<PlaylistInitialMode, string> = {
    combine: changes,
    platform: `Cantaro: ${preview.additions} added, ${preview.removals} removed`,
    cantaro: `${platform}: ${preview.removals} added, ${preview.additions} removed`,
  };
  return <div className="flex items-center justify-between gap-2 border border-warning-border bg-warning-surface p-3">
    <div role="status" className="min-w-0 space-y-1">
      <p className="text-sm font-bold text-content">{preview.remoteName}: {preview.remoteTrackCount} tracks; Cantaro: {preview.cantaroTrackCount}</p>
      <p className="text-sm text-warning-content">{copy[mode]}</p>
    </div>
    {preview.unavailableItemCount ? <UnavailableEntriesInfo count={preview.unavailableItemCount} service={preview.service} /> : null}
  </div>;
}

function AttachButtons({ choices, disabled, busy, onClose }: { choices: Choices; disabled: boolean; busy: boolean; onClose: () => void }) {
  return <div className="flex flex-wrap gap-2">
    {choices.preview ? <button type="button" disabled={disabled} onClick={() => void choices.attach()} className="min-h-11 bg-action px-4 text-sm font-bold text-action-content disabled:opacity-50">Confirm link</button>
      : <button type="button" disabled={!choices.selectedId || disabled} onClick={() => void choices.review()} className="min-h-11 bg-action px-4 text-sm font-bold text-action-content disabled:opacity-50">{choices.reviewing ? 'Loading...' : 'Review first sync'}</button>}
    <button type="button" disabled={busy} onClick={() => { if (choices.preview) choices.setPreview(null); else onClose(); }} className="min-h-11 px-3 text-sm font-semibold text-content underline focus-visible:outline-2 focus-visible:outline-focus">{choices.preview ? 'Change choices' : 'Cancel'}</button>
  </div>;
}

function AttachMessages({ candidatesError, choicesError }: { candidatesError: string | null; choicesError: string | null }) {
  return <>
    {candidatesError ? <p role="alert" className="text-sm text-danger-content">{candidatesError}</p> : null}
    {choicesError ? <p role="alert" className="text-sm text-danger-content">{choicesError}</p> : null}
  </>;
}

function AttachReviewArea({ choices, disabled, busy, candidatesError, onClose }: {
  choices: Choices;
  disabled: boolean;
  busy: boolean;
  candidatesError: string | null;
  onClose: () => void;
}) {
  return <>
    {choices.preview ? <InitialPreview preview={choices.preview} mode={choices.initialMode} /> : null}
    <AttachButtons choices={choices} disabled={disabled || Boolean(candidatesError)} busy={busy} onClose={onClose} />
  </>;
}

export function MusicPlaylistAttachForm({ playlistId, playlistName, service, busy, onAttached, onClose, preselected }: {
  playlistId: string;
  playlistName: string;
  service: PlaylistSyncService;
  busy: boolean;
  onAttached: AttachAction;
  onClose: () => void;
  preselected?: PlaylistCreateCandidate;
}) {
  const candidates = useAttachCandidates(service, preselected);
  const choices = useAttachChoices(playlistId, service, onAttached, onClose, preselected?.servicePlaylistId);
  const disabled = busy || candidates.loading || choices.reviewing;
  return <div className="ml-auto w-full max-w-xl space-y-3 border border-border-subtle bg-surface-raised p-4">
    {candidates.loading ? <p role="status" className="text-sm text-content-muted">Loading playlists...</p> : <AttachChoicesFields service={service} playlistName={playlistName} choices={choices} candidates={candidates.candidates} disabled={disabled || Boolean(choices.preview)} />}
    <AttachMessages candidatesError={candidates.error} choicesError={choices.error} />
    <AttachReviewArea choices={choices} disabled={disabled} busy={busy} candidatesError={candidates.error} onClose={onClose} />
  </div>;
}

export function MusicPlaylistAttach({ playlistId, playlistName, service, busy, onAttached }: {
  playlistId: string;
  playlistName: string;
  service: PlaylistSyncService;
  busy: boolean;
  onAttached: AttachAction;
}) {
  const { open, setOpen, triggerRef, panelRef, panelId } = useDismissiblePanel();
  return <div className="w-full text-right">
    <button type="button" ref={triggerRef} aria-expanded={open} aria-controls={panelId} disabled={busy} onClick={() => setOpen(!open)} className="min-h-11 border border-border-strong px-3 text-sm font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">Link existing</button>
    {open ? <div ref={panelRef} id={panelId} className="pt-3 text-left"><MusicPlaylistAttachForm playlistId={playlistId} playlistName={playlistName} service={service} busy={busy} onAttached={onAttached} onClose={() => { setOpen(false); triggerRef.current?.focus(); }} /></div> : null}
  </div>;
}
