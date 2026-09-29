import { Link } from '@tanstack/react-router';
import { Check, Link2, LoaderCircle, RefreshCw, TriangleAlert } from 'lucide-react';
import type { PlaylistSyncInfo } from '@cantaro/client-shared/music';
import type { usePlatformPlaylistSync } from './usePlatformPlaylistSync';

type PlaylistSyncState = ReturnType<typeof usePlatformPlaylistSync>;

function mappingDescription(mapping: PlaylistSyncInfo) {
  const statuses: Record<string, string> = {
    partial_failure: 'Some tracks could not sync', error: 'Last sync failed',
    rate_limited: 'Waiting for platform', quota_limited: 'Spotify quota reached', pending: 'Sync pending', running: 'Sync in progress',
  };
  const current = statuses[mapping.lastSyncStatus ?? ''];
  if (current) return `${current}. Open Cantaro playlist`;
  if (mapping.lastSyncStatus !== 'success' || !mapping.lastSyncedAt) return 'Linked to Cantaro. Open playlist';
  return `Last synced ${new Date(mapping.lastSyncedAt).toLocaleString()}. Open Cantaro playlist`;
}

const mappingIcons: Record<string, typeof Check> = { success: Check, error: TriangleAlert, partial_failure: TriangleAlert, pending: RefreshCw, running: RefreshCw, rate_limited: RefreshCw, quota_limited: RefreshCw };

function PlaylistSyncLink({ mapping }: { mapping: PlaylistSyncInfo }) {
  const failed = mapping.lastSyncStatus === 'error' || mapping.lastSyncStatus === 'partial_failure';
  const Icon = mappingIcons[mapping.lastSyncStatus ?? ''] ?? Link2;
  const label = mappingDescription(mapping);
  return (
    <Link to="/music/playlists/$playlistId" params={{ playlistId: mapping.playlistId }} title={label} aria-label={`${mapping.name}. ${label}`}
      className={`inline-flex h-11 w-11 shrink-0 items-center justify-center transition hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus ${failed ? 'text-warning-content' : 'text-success-content'}`}>
      <Icon className="h-5 w-5" aria-hidden="true" />
    </Link>
  );
}

export function MusicPlatformPlaylistSyncAction({ playlistId, title, sync }: {
  playlistId: string;
  title: string;
  sync: PlaylistSyncState;
}) {
  if (sync.pending.has(playlistId)) {
    return <span role="status" title="Syncing to Cantaro" className="inline-flex h-11 w-11 shrink-0 items-center justify-center text-content-muted"><LoaderCircle aria-hidden="true" className="h-5 w-5 animate-spin motion-reduce:animate-none" /><span className="sr-only">Syncing {title} to Cantaro</span></span>;
  }
  const mapping = sync.mappings.get(playlistId);
  if (mapping) return <PlaylistSyncLink mapping={mapping} />;
  if (sync.isLoading || sync.loadError) {
    return <span className="inline-flex h-11 w-11 shrink-0 items-center justify-center text-content-muted" title="Sync status unavailable"><LoaderCircle aria-hidden="true" className="h-4 w-4" /><span className="sr-only">Sync status unavailable</span></span>;
  }
  return (
    <button type="button" onClick={() => void sync.startSync(playlistId)} title={`Sync ${title} to Cantaro`} aria-label={`Sync ${title} to Cantaro`}
      className="inline-flex min-h-11 shrink-0 items-center gap-1.5 px-2 text-xs font-bold text-personal-accent-strong transition hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus">
      <RefreshCw className="h-4 w-4" aria-hidden="true" /> Sync
    </button>
  );
}

export function MusicPlatformPlaylistSyncError({ playlistId, sync }: { playlistId: string; sync: PlaylistSyncState }) {
  const error = sync.errors[playlistId];
  if (!error) return null;
  return (
    <div role="alert" className="px-3 py-2 text-xs text-danger-content">
      <p>{error}</p>
      {sync.pending.has(playlistId) ? <button type="button" onClick={() => void sync.reload()} className="min-h-11 font-bold underline focus-visible:outline-2 focus-visible:outline-focus">Check status</button> : null}
    </div>
  );
}
