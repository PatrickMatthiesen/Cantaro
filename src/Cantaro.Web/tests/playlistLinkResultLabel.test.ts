import { describe, expect, test } from 'bun:test';
import { isPlaylistProviderCooldown, playlistLinkResultLabel, playlistLinkSyncActivity, playlistLinkSyncButton } from '../src/music/playlistLinkResultLabel';

describe('playlist link result label', () => {
  const active = (lastSyncStatus: string | null, service: 'youtube' | 'spotify' = 'youtube') => ({ service, state: 'active', lastSyncStatus });

  test('shows the latest sync result for active links', () => {
    expect(playlistLinkResultLabel(active('success'))).toBe('Synced');
    expect(playlistLinkResultLabel(active('pending'))).toBe('Sync queued');
    expect(playlistLinkResultLabel(active('error'))).toBe('Sync failed');
    expect(playlistLinkResultLabel(active('rate_limited'))).toBe('Waiting for YouTube');
    expect(playlistLinkResultLabel(active('quota_limited', 'spotify'))).toBe('Spotify quota reached');
    expect(isPlaylistProviderCooldown('quota_limited')).toBe(true);
  });

  test('distinguishes queued work from active syncing and provider waits', () => {
    expect(playlistLinkSyncActivity(active('pending'))).toBe('queued');
    expect(playlistLinkSyncActivity(active('running'))).toBe('syncing');
    expect(playlistLinkResultLabel(active('running'))).toBe('Syncing');
    for (const status of ['rate_limited', 'quota_limited', 'success', 'error', null]) {
      expect(playlistLinkSyncActivity(active(status))).toBe('idle');
    }
    expect(playlistLinkSyncActivity({ ...active('running'), reconnectRequired: true })).toBe('idle');
    expect(playlistLinkSyncActivity({ ...active('running'), state: 'paused' })).toBe('idle');
  });

  test('uses the first sync fallback only when the active link has no result', () => {
    expect(playlistLinkResultLabel(active(null))).toBe('Pending first sync');
    expect(playlistLinkResultLabel({ ...active('success'), state: 'creating' })).toBe('Creating playlist');
    expect(playlistLinkResultLabel({ ...active('rate_limited'), state: 'creation_failed' })).toBe('Waiting for YouTube');
    expect(playlistLinkResultLabel({ ...active('quota_limited', 'spotify'), state: 'creation_failed' })).toBe('Spotify quota reached');
  });

  test('keeps the sync button animated after queuing completes, but stops during cooldown', () => {
    expect(playlistLinkSyncButton(active('success'), true)).toEqual({ label: 'Queuing...', spinning: true });
    expect(playlistLinkSyncButton(active('pending'), false)).toEqual({ label: 'Queued', spinning: false });
    expect(playlistLinkSyncButton(active('running'), false)).toEqual({ label: 'Syncing', spinning: true });
    for (const status of ['rate_limited', 'quota_limited', 'success', 'error']) {
      expect(playlistLinkSyncButton(active(status), false)).toEqual({ label: 'Sync now', spinning: false });
    }
  });
});
