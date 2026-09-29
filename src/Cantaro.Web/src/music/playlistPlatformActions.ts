import type { PlaylistSyncInfo } from '@cantaro/client-shared/music';

export function playlistPlatformAction(mapping: PlaylistSyncInfo | undefined): 'create' | 'refresh' | 'update' | 'review' {
  if (!mapping) return 'create';
  if (mapping.servicePlaylistId.startsWith('pending:')) return 'review';
  return mapping.syncMode === 'from_cantaro' ? 'update' : 'refresh';
}
