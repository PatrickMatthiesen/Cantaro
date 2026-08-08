import { describe, expect, it } from 'bun:test';
import {
  readStreamingServicePreference,
  STREAMING_SERVICE_PREFERENCE_KEY,
  writeStreamingServicePreference,
  type StorageLike,
} from '@cantaro/client-shared/media';

class MemoryStorage implements StorageLike {
  readonly values = new Map<string, string>();
  getItem(key: string) { return this.values.get(key) ?? null; }
  setItem(key: string, value: string) { this.values.set(key, value); }
  removeItem(key: string) { this.values.delete(key); }
}

describe('streaming service preference', () => {
  it('round-trips the preferred service in the versioned document', () => {
    const storage = new MemoryStorage();
    writeStreamingServicePreference('hidive', storage);

    expect(readStreamingServicePreference(storage)).toEqual({
      version: 1,
      preferredServiceId: 'hidive',
    });
    expect(JSON.parse(storage.getItem(STREAMING_SERVICE_PREFERENCE_KEY) ?? '')).toEqual({
      version: 1,
      preferredServiceId: 'hidive',
    });
  });

  it('falls back safely for unknown versions, service IDs, and corrupt JSON', () => {
    const storage = new MemoryStorage();
    for (const value of [
      '{broken',
      JSON.stringify({ version: 2, preferredServiceId: 'netflix' }),
      JSON.stringify({ version: 1, preferredServiceId: 'not-a-service' }),
    ]) {
      storage.setItem(STREAMING_SERVICE_PREFERENCE_KEY, value);
      expect(readStreamingServicePreference(storage)).toEqual({
        version: 1,
        preferredServiceId: null,
      });
    }
  });
});
