import { Link } from '@tanstack/react-router';
import { type PlaylistCreateCandidate, type PlaylistSyncService } from '@cantaro/client-shared/music';
import type { RefObject } from 'react';
import { platformName } from './musicPresentation';
import type { PlaylistCreateReviewState } from './usePlaylistCreateReview';

function trackCount(count: number) { return `${count} ${count === 1 ? 'track' : 'tracks'}`; }

function knownSharedCount(count: number | null) {
  if (count === null) return 'Known matches unavailable';
  return `${count} known ${count === 1 ? 'shared track' : 'shared tracks'}`;
}

function CandidateRow({ candidate, creating, onSelect }: {
  candidate: PlaylistCreateCandidate;
  creating: boolean;
  onSelect: (candidate: PlaylistCreateCandidate) => void;
}) {
  return <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-border-subtle p-2 last:border-0">
    <div className="min-w-0 flex-1">
      <p className="text-sm font-medium wrap-anywhere text-content">{candidate.name}</p>
      <p className="text-xs text-content-muted">{trackCount(candidate.remoteTrackCount)} · {knownSharedCount(candidate.sharedTrackCount)}</p>
      {candidate.comparisonError ? <p className="text-xs text-warning-content">{candidate.comparisonError}</p> : null}
    </div>
    {candidate.linkedPlaylistId ? <Link to="/music/playlists/$playlistId" params={{ playlistId: candidate.linkedPlaylistId }} className="inline-flex min-h-11 items-center text-xs font-semibold text-content-muted underline focus-visible:outline-2 focus-visible:outline-focus">Already linked</Link>
      : <button type="button" disabled={creating} onClick={(event) => { event.stopPropagation(); onSelect(candidate); }} className="min-h-11 border border-border-strong px-3 text-xs font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">Link existing</button>}
  </div>;
}

function CandidateChoices({ service, state, onSelect }: {
  service: PlaylistSyncService;
  state: PlaylistCreateReviewState;
  onSelect: (candidate: PlaylistCreateCandidate) => void;
}) {
  const candidates = state.preview?.candidates ?? [];
  if (!candidates.length) return null;
  return <>
    <p className="mb-2 text-sm font-semibold text-content">Same-name {platformName(service)} playlists</p>
    <div className="max-h-64 overflow-y-auto overscroll-contain border border-border-subtle">
      {candidates.map((candidate) => <CandidateRow key={candidate.servicePlaylistId} candidate={candidate} creating={state.creating} onSelect={onSelect} />)}
    </div>
    <button type="button" disabled={state.creating} onClick={() => void state.createReviewed()} className="mt-2 min-h-11 px-2 text-sm font-semibold text-content-muted underline focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">Create new playlist</button>
  </>;
}

function PanelError({ state }: { state: PlaylistCreateReviewState }) {
  if (!state.error) return null;
  return <>
    <p role="alert" className="text-sm text-danger-content">{state.error}</p>
    {!state.preview ? <button type="button" onClick={() => void state.start()} className="min-h-11 text-sm font-semibold text-content underline">Try again</button> : null}
  </>;
}

function PlaylistCreateReviewPanel({ service, state, onSelect }: {
  service: PlaylistSyncService;
  state: PlaylistCreateReviewState;
  onSelect: (candidate: PlaylistCreateCandidate) => void;
}) {
  if (!state.preview && !state.error) return null;
  return <div className="ml-auto w-full max-w-xl border border-border-strong bg-surface-raised p-3 text-left">
    <CandidateChoices service={service} state={state} onSelect={onSelect} />
    <PanelError state={state} />
  </div>;
}

export function PlaylistCreatePanel({ open, panelRef, panelId, service, state, onSelect }: {
  open: boolean; panelRef: RefObject<HTMLDivElement | null>; panelId: string;
  service: PlaylistSyncService; state: PlaylistCreateReviewState;
  onSelect: (candidate: PlaylistCreateCandidate) => void;
}) {
  if (!open) return null;
  return <div ref={panelRef} id={panelId} className="pt-3"><PlaylistCreateReviewPanel service={service} state={state} onSelect={onSelect} /></div>;
}

export function PlaylistCreateTrigger({ label, state, open, busy, triggerRef, panelId, onStart }: {
  label: string;
  state: PlaylistCreateReviewState;
  open: boolean;
  busy: boolean;
  triggerRef: RefObject<HTMLButtonElement | null>;
  panelId: string;
  onStart?: () => void;
}) {
  function toggle() {
    if (open) state.close();
    else { onStart?.(); void state.start(); }
  }
  const text = state.checking ? 'Checking playlists...' : state.creating ? 'Creating playlist...' : label;
  return <button type="button" disabled={busy || state.checking || state.creating} ref={triggerRef} onClick={toggle}
    aria-expanded={open} aria-controls={panelId}
    className="min-h-11 border border-border-strong px-3 text-sm font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">{text}</button>;
}
