import { afterEach, describe, expect, it, vi } from 'vitest';
import { handleContentPreferencesRequest, initializeContentPreferences, readContentPreferences } from './contentPreferences';
import { SETTINGS_STORAGE_KEY } from './settingsRepository';

afterEach(() => vi.unstubAllGlobals());

describe('content preferences bridge', () => {
  it('returns only preferences, without server or credential storage', async () => {
    vi.stubGlobal('browser', { storage: { local: {
      get: vi.fn(async (key: string) => key === SETTINGS_STORAGE_KEY
        ? { [key]: { baseUrl: 'https://private.example', verboseLogging: true } }
        : { [key]: { disabledUntil: '2999-01-01T00:00:00Z' } }),
    } } });
    const result = await handleContentPreferencesRequest({ type: 'content.preferences', correlationId: 'preferences' });
    expect(result).toEqual({ ok: true, correlationId: 'preferences', value: { verboseLogging: true, trackingPaused: true } });
  });

  it('reads through runtime messaging with no content storage access', async () => {
    const sendMessage = vi.fn(async () => ({ ok: true, value: { verboseLogging: false, trackingPaused: false } }));
    vi.stubGlobal('browser', { runtime: { sendMessage } });
    await expect(readContentPreferences()).resolves.toEqual({ verboseLogging: false, trackingPaused: false });
    expect(sendMessage).toHaveBeenCalledWith({ type: 'content.preferences', correlationId: expect.any(String) });
  });

  it('notifies supported tabs on preference changes', async () => {
    let onChanged: (changes: Record<string, unknown>, areaName: string) => void = () => {};
    const sendMessage = vi.fn(async () => undefined);
    vi.stubGlobal('browser', {
      storage: { onChanged: { addListener: vi.fn(listener => { onChanged = listener; }) } },
      tabs: { query: vi.fn(async () => [{ id: 7 }]), sendMessage },
    });
    initializeContentPreferences();
    onChanged({ unrelated: {} }, 'local');
    expect(sendMessage).not.toHaveBeenCalled();
    onChanged({ [SETTINGS_STORAGE_KEY]: {} }, 'local');
    await vi.waitFor(() => expect(sendMessage).toHaveBeenCalledWith(7, { type: 'content.preferences.changed' }));
  });
});
