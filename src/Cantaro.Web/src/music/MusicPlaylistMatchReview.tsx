import { playlistSyncApi, type PlaylistMatchCandidate, type PlaylistSyncLink, type PlaylistUnmatchedEntry } from '@cantaro/client-shared/music';
import { ArrowLeft, ArrowUpRight, ChevronRight, Search, X } from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { platformName } from './musicPresentation';
import { playlistMatchSearchQuery } from './playlistMatchSearchQuery';
import { usePlaylistMatchReview } from './usePlaylistMatchReview';

const actionClass = 'inline-flex min-h-11 items-center justify-center gap-2 border border-border-strong px-3 text-sm font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50';

function duration(seconds: number | null) {
  return seconds == null ? 'Unknown length' : `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function Recording({ title, artist, durationSeconds, url }: { title: string; artist: string; durationSeconds: number | null; url: string | null }) {
  return <div className="min-w-0">
    {url ? <a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-8 max-w-full items-center gap-1.5 text-sm font-semibold text-content hover:underline focus-visible:outline-2 focus-visible:outline-focus"><span className="wrap-anywhere">{title}</span><ArrowUpRight className="size-3.5 shrink-0" aria-hidden /><span className="sr-only"> (opens on platform)</span></a> : <p className="text-sm font-semibold wrap-anywhere text-content">{title}</p>}
    <p className="text-xs leading-relaxed wrap-anywhere text-content-muted">{artist} · <span className="whitespace-nowrap tabular-nums">{duration(durationSeconds)}</span></p>
  </div>;
}

function CandidateRow({ candidate, summary, disabled, onChoose }: { candidate: PlaylistMatchCandidate; summary: string; disabled: boolean; onChoose: () => void }) {
  return <li className="grid items-start gap-2 border-t border-border-subtle py-3 sm:grid-cols-[minmax(0,1fr)_auto]">
    <div className="min-w-0"><Recording {...candidate} />{candidate.reason && candidate.reason !== summary ? <p className="mt-1 text-xs leading-relaxed text-warning-content">{candidate.reason}</p> : null}</div>
    <button type="button" disabled={disabled} onClick={onChoose} className={actionClass} aria-label={`Choose ${candidate.title} by ${candidate.artist}`}>Choose</button>
  </li>;
}

type ReviewState = ReturnType<typeof usePlaylistMatchReview>;

function MatchConfirmation({ state, service }: { state: ReviewState; service: string }) {
  const titleRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { titleRef.current?.focus(); }, []);
  if (!state.selected) return null;
  return <div className="space-y-3 border-t border-border-subtle pt-3">
    <h4 ref={titleRef} tabIndex={-1} className="text-sm font-semibold text-content focus-visible:outline-2 focus-visible:outline-focus">Is this the same recording?</h4>
    <Recording {...state.selected} />
    {state.selected.reason ? <p className="text-sm text-warning-content">{state.selected.reason}</p> : null}
    <p className="max-w-prose text-xs leading-relaxed text-content-muted">This saves the match across your library and syncs this playlist to {platformName(service)}. Different edits, remixes and covers should stay separate.</p>
    <div className="flex flex-wrap gap-2">
      <button type="button" disabled={state.saving} onClick={() => void state.confirm()} className="min-h-11 bg-action px-4 text-sm font-semibold text-action-content hover:bg-action-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50">{state.saving ? 'Saving and syncing...' : 'Confirm match and sync'}</button>
      <button type="button" disabled={state.saving} onClick={() => state.setSelected(null)} className={actionClass}>Back to results</button>
    </div>
  </div>;
}

function MatchSearch({ state, service, initialQuery, disabled }: { state: ReviewState; service: string; initialQuery: string; disabled: boolean }) {
  const [editedQuery, setEditedQuery] = useState<string | null>(null);
  const suggestedQuery = state.result?.suggestedQuery ?? initialQuery;
  const query = editedQuery ?? suggestedQuery;
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  useEffect(() => { inputRef.current?.focus(); }, []);
  return <>
    <form onSubmit={(event) => { event.preventDefault(); if (query.trim()) void state.search(playlistMatchSearchQuery(query, suggestedQuery)); }} className="space-y-2">
      <label htmlFor={inputId} className="text-sm font-medium text-content">Search {platformName(service)}</label>
      <div className="flex flex-wrap gap-2">
        <input ref={inputRef} id={inputId} value={query} maxLength={200} disabled={disabled} onChange={(event) => setEditedQuery(event.target.value)} className="min-h-11 min-w-0 flex-1 border border-border-strong bg-surface px-3 text-sm text-content focus-visible:outline-2 focus-visible:outline-focus" />
        <button type="submit" disabled={disabled || state.searching || !query.trim()} className={actionClass}><Search className="size-4" aria-hidden />Search</button>
      </div>
    </form>
    <SearchResults state={state} service={service} disabled={disabled} />
  </>;
}

function SearchResults({ state, service, disabled }: { state: ReviewState; service: string; disabled: boolean }) {
  if (state.searching) return <p role="status" className="py-3 text-sm text-content-muted">Searching {platformName(service)}...</p>;
  if (!state.result) return null;
  return <>
      <p role="status" className="text-sm leading-relaxed text-warning-content">{state.result.reason}</p>
      {state.result.candidates.length > 0 ? <ul aria-label="Possible matches">{state.result.candidates.map((candidate) => <CandidateRow key={candidate.externalId} candidate={candidate} summary={state.result!.reason} disabled={disabled} onChoose={() => state.setSelected(candidate)} />)}</ul> : <p className="text-xs leading-relaxed text-content-muted">Try another title or artist. The track stays in Cantaro and will be searched again on the next sync.</p>}
  </>;
}

function ReviewBody({ state, entry, service, busy }: { state: ReviewState; entry: PlaylistUnmatchedEntry; service: string; busy: boolean }) {
  if (state.saved) return <SavedMatch state={state} busy={busy} />;
  if (state.selected) return <MatchConfirmation state={state} service={service} />;
  return <MatchSearch state={state} service={service} initialQuery={`${entry.title} ${entry.artist}`} disabled={busy || state.saving} />;
}

function SavedMatch({ state, busy }: { state: ReviewState; busy: boolean }) {
  const statusRef = useRef<HTMLParagraphElement>(null);
  useEffect(() => { statusRef.current?.focus(); }, []);
  return <div className="flex flex-wrap items-center gap-3"><p ref={statusRef} tabIndex={-1} role="status" className="text-sm text-success-content focus-visible:outline-2 focus-visible:outline-focus">Match saved.</p><button type="button" disabled={busy || state.saving} onClick={() => void state.confirm()} className={actionClass}>{state.saving ? 'Queuing...' : 'Sync playlist'}</button></div>;
}

function EntryReview({ playlistId, link, entry, busy, onSync, onResolved, onBack }: {
  playlistId: string; link: PlaylistSyncLink; entry: PlaylistUnmatchedEntry; busy: boolean;
  onSync: () => Promise<boolean>; onResolved: () => void; onBack: () => void;
}) {
  const state = usePlaylistMatchReview(playlistId, link.mappingId, entry.entryId, onSync, onResolved);
  return <div className="space-y-3">
    <button type="button" disabled={state.saving} onClick={onBack} className="inline-flex min-h-11 items-center gap-2 text-sm font-medium text-content-muted hover:text-content focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50"><ArrowLeft className="size-4" aria-hidden />Unmatched tracks</button>
    <div className="border-b border-border-subtle pb-3"><Recording {...entry} url={entry.sourceUrl} /></div>
    {state.error ? <p role="alert" className="text-sm text-danger-content">{state.error}</p> : null}
    <ReviewBody state={state} entry={entry} service={link.service} busy={busy} />
  </div>;
}

function useUnmatchedEntries(playlistId: string, mappingId: string) {
  const [entries, setEntries] = useState<PlaylistUnmatchedEntry[]>([]);
  const [selected, setSelected] = useState<PlaylistUnmatchedEntry | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  useEffect(() => {
    let active = true;
    void playlistSyncApi.unmatched(playlistId, mappingId).then((response) => {
      if (active) { setEntries(response); setError(null); }
    }).catch((failure) => { if (active) setError(failure instanceof Error ? failure.message : 'Could not load unmatched tracks.'); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [playlistId, mappingId, reloadKey]);
  const resolved = useCallback(() => { setSelected(null); setLoading(true); setReloadKey((value) => value + 1); }, []);
  return { entries, selected, setSelected, loading, error, resolved };
}

function UnmatchedList({ state, busy }: { state: ReturnType<typeof useUnmatchedEntries>; busy: boolean }) {
  if (state.loading) return <p role="status" className="py-3 text-sm text-content-muted">Loading unmatched tracks...</p>;
  if (state.error) return <p role="alert" className="py-3 text-sm text-danger-content">{state.error} <button type="button" onClick={state.resolved} className="underline focus-visible:outline-2 focus-visible:outline-focus">Retry</button></p>;
  if (state.entries.length === 0) return <p role="status" className="py-3 text-sm text-success-content">All tracks have a match.</p>;
  return <ul>{state.entries.map((entry) => <li key={entry.entryId} className="border-t border-border-subtle"><button type="button" disabled={busy} onClick={() => state.setSelected(entry)} className="flex min-h-16 w-full items-center justify-between gap-3 px-2 py-3 text-left hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50"><span className="min-w-0"><span className="block text-sm font-medium wrap-anywhere text-content">{entry.title}</span><span className="mt-1 block text-xs wrap-anywhere text-content-muted">{entry.artist} · {duration(entry.durationSeconds)}</span></span><span className="flex shrink-0 items-center gap-1 text-xs font-medium text-content">Find match<ChevronRight className="size-4" aria-hidden /></span></button></li>)}</ul>;
}

export function MusicPlaylistMatchReview({ playlistId, link, busy, onSync, onClose, panelId }: {
  playlistId: string; link: PlaylistSyncLink; busy: boolean; onSync: () => Promise<boolean>; onClose: () => void; panelId: string;
}) {
  const state = useUnmatchedEntries(playlistId, link.mappingId);
  const titleRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { if (!state.selected) titleRef.current?.focus(); }, [state.selected]);
  return <section id={panelId} aria-label={`${platformName(link.service)} unmatched tracks`} className="mt-4 min-w-0 border-t border-border-subtle pt-3">
    <div className="flex items-center justify-between gap-3">
      <h3 ref={titleRef} tabIndex={-1} className="text-sm font-semibold text-content focus-visible:outline-2 focus-visible:outline-focus">Unmatched on {platformName(link.service)}</h3>
      <button type="button" disabled={busy} aria-label="Close unmatched tracks" onClick={onClose} className="flex size-11 shrink-0 items-center justify-center text-content-muted hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus disabled:opacity-50"><X className="size-4" aria-hidden /></button>
    </div>
    {state.selected ? <EntryReview key={state.selected.entryId} playlistId={playlistId} link={link} entry={state.selected} busy={busy} onSync={onSync} onResolved={state.resolved} onBack={() => state.setSelected(null)} /> : <UnmatchedList state={state} busy={busy} />}
  </section>;
}
