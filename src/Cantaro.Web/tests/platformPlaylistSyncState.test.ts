import { describe, expect, test } from 'bun:test';
import type { MusicSyncJobResponse, PlaylistSyncInfo, SyncStatusResponse } from '../../Cantaro.ClientShared/src/music/services/syncApi';
import { importBlockReason, isPlaylistBusy, jobFailureMessage, pendingPlaylistIds, syncMappings } from '../src/music/platformPlaylistSyncState';

function mapping(service: string, servicePlaylistId: string, syncMode: string): PlaylistSyncInfo {
  return {
    playlistId: `${service}-${servicePlaylistId}`,
    name: 'Playlist',
    service,
    servicePlaylistId,
    syncMode,
    lastSyncedAt: null,
    lastSyncStatus: 'success',
  };
}

function status(playlists: PlaylistSyncInfo[]): SyncStatusResponse {
  return { playlists, overall: { canSyncNow: true } } as SyncStatusResponse;
}

function job(jobStatus: MusicSyncJobResponse['status'], results: MusicSyncJobResponse['results'] = []): MusicSyncJobResponse {
  return { status: jobStatus, results, failureCount: results.filter((result) => !result.success).length } as MusicSyncJobResponse;
}

describe('platform playlist sync state', () => {
  test('filters mappings by service even when external IDs match', () => {
    const youtube = mapping('youtube', 'shared-id', 'import_only');
    const spotify = mapping('spotify', 'shared-id', 'from_cantaro');
    const mappings = syncMappings(status([youtube, spotify]), 'youtube');

    expect(mappings.size).toBe(1);
    expect(mappings.get('shared-id')).toEqual(youtube);
  });

  test('blocks import of a Cantaro export and when status cannot be loaded', () => {
    const currentStatus = status([]);
    expect(importBlockReason(currentStatus, null, mapping('spotify', 'playlist', 'from_cantaro'))).toContain('cannot be imported');
    expect(importBlockReason(currentStatus, null, undefined, true)).toContain('Wait');
    expect(importBlockReason(null, 'Status request failed', undefined)).toBe('Status request failed');
    expect(importBlockReason(currentStatus, null, mapping('spotify', 'playlist', 'import_only'))).toBeNull();
  });

  test('keeps an immediate second click and a queued job pending', () => {
    const jobs = new Map<string, MusicSyncJobResponse>();
    const submitting = new Set(['playlist']);
    expect(isPlaylistBusy('playlist', submitting, jobs)).toBe(true);
    submitting.clear();
    jobs.set('playlist', job('queued'));
    expect(isPlaylistBusy('playlist', submitting, jobs)).toBe(true);
    expect(pendingPlaylistIds(submitting, jobs).has('playlist')).toBe(true);
    jobs.set('playlist', job('completed'));
    expect(isPlaylistBusy('playlist', submitting, jobs)).toBe(false);
    expect(pendingPlaylistIds(submitting, jobs).has('playlist')).toBe(false);
  });

  test('shows a failed playlist result even if the terminal job says completed', () => {
    const failed = job('completed', [{
      servicePlaylistId: 'playlist', playlistName: 'Playlist', success: false,
      errorMessage: 'Track has no match.', retryable: true,
    }]);
    failed.failureCount = 0;
    expect(jobFailureMessage(failed, 'playlist')).toBe('Track has no match.');
    expect(jobFailureMessage(job('completed'), 'playlist')).toBeNull();
  });
});
