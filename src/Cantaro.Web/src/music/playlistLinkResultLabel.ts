import type { PlaylistSyncLink } from '@cantaro/client-shared/music';
import { platformName } from './musicPresentation';

type LinkStatus = Pick<PlaylistSyncLink, 'service' | 'state' | 'lastSyncStatus' | 'reconnectRequired'>;

export function playlistLinkSyncActivity(link: LinkStatus) {
  if (link.reconnectRequired || link.state !== 'active') return 'idle';
  if (link.lastSyncStatus === 'running') return 'syncing';
  return link.lastSyncStatus === 'pending' ? 'queued' : 'idle';
}

export function playlistLinkSyncButton(link: LinkStatus, queuing: boolean) {
  const activity = playlistLinkSyncActivity(link);
  if (activity === 'syncing') return { label: 'Syncing', spinning: true };
  if (queuing) return { label: 'Queuing...', spinning: true };
  return { label: activity === 'queued' ? 'Queued' : 'Sync now', spinning: false };
}

export function isPlaylistProviderCooldown(status: string | null | undefined) {
  return status === 'rate_limited' || status === 'quota_limited';
}

function waitingLabel(link: LinkStatus) {
  return link.lastSyncStatus === 'quota_limited' && link.service === 'spotify'
    ? 'Spotify quota reached'
    : `Waiting for ${platformName(link.service)}`;
}

export function playlistLinkResultLabel(link: LinkStatus) {
  if (link.reconnectRequired) return 'Reconnect required';
  const stateLabels: Record<string, string> = {
    reconnect_required: 'Reconnect required', missing: 'Remote playlist missing',
    creation_uncertain: 'Playlist creation needs review', creating: 'Creating playlist',
    creation_failed: isPlaylistProviderCooldown(link.lastSyncStatus) ? waitingLabel(link) : 'Playlist creation failed',
  };
  const syncLabels: Record<string, string> = {
    success: 'Synced', partial: 'Partially synced', pending: 'Sync queued',
    running: 'Syncing', rate_limited: `Waiting for ${platformName(link.service)}`,
    quota_limited: waitingLabel(link),
    order_conflict: 'Order needs review', error: 'Sync failed',
  };
  const fallbackLabels: Record<string, string> = { active: 'Pending first sync' };
  return stateLabels[link.state] ?? syncLabels[link.lastSyncStatus ?? '']
    ?? fallbackLabels[link.state] ?? link.state.replaceAll('_', ' ');
}
