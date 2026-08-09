import { useCallback, useSyncExternalStore } from 'react';
import { isStreamingServiceId, type StreamingServiceId } from './streamingServices';

export const STREAMING_SERVICE_PREFERENCE_KEY = 'cantaro.media.preferredStreamingService.v1';
const PREFERENCE_EVENT = 'cantaro:streaming-service-preference';

export interface StreamingServicePreference {
  version: 1;
  preferredServiceId: StreamingServiceId | null;
}

export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

const EMPTY_PREFERENCE: StreamingServicePreference = { version: 1, preferredServiceId: null };

export function readStreamingServicePreference(storage?: StorageLike | null): StreamingServicePreference {
  if (!storage) return EMPTY_PREFERENCE;
  try {
    return parsePreference(storage.getItem(STREAMING_SERVICE_PREFERENCE_KEY));
  } catch {
    return EMPTY_PREFERENCE;
  }
}

function parsePreference(raw: string | null): StreamingServicePreference {
  const parsed: unknown = JSON.parse(raw ?? 'null');
  if (!isVersionOnePreference(parsed)) return EMPTY_PREFERENCE;
  return parsed.preferredServiceId === null || isStreamingServiceId(parsed.preferredServiceId)
    ? { version: 1, preferredServiceId: parsed.preferredServiceId }
    : EMPTY_PREFERENCE;
}

function isVersionOnePreference(value: unknown): value is Record<string, unknown> & { version: 1 } {
  return Boolean(value && typeof value === 'object' && 'version' in value && value.version === 1);
}

export function writeStreamingServicePreference(
  preferredServiceId: StreamingServiceId | null,
  storage?: StorageLike | null,
): void {
  if (!storage) return;
  storage.setItem(STREAMING_SERVICE_PREFERENCE_KEY, JSON.stringify({
    version: 1,
    preferredServiceId,
  } satisfies StreamingServicePreference));
}

export function useStreamingServicePreference(): readonly [
  StreamingServiceId | null,
  (serviceId: StreamingServiceId | null) => void,
] {
  const preference = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  const setPreference = useCallback((serviceId: StreamingServiceId | null) => {
    writeStreamingServicePreference(serviceId, browserStorage());
    window.dispatchEvent(new Event(PREFERENCE_EVENT));
  }, []);
  return [preference.preferredServiceId, setPreference] as const;
}

function subscribe(onChange: () => void): () => void {
  if (typeof window === 'undefined') return () => undefined;
  const onStorage = (event: StorageEvent) => {
    if (event.key === STREAMING_SERVICE_PREFERENCE_KEY) onChange();
  };
  window.addEventListener('storage', onStorage);
  window.addEventListener(PREFERENCE_EVENT, onChange);
  return () => {
    window.removeEventListener('storage', onStorage);
    window.removeEventListener(PREFERENCE_EVENT, onChange);
  };
}

let cachedRaw: string | null | undefined;
let cachedPreference = EMPTY_PREFERENCE;

function getSnapshot(): StreamingServicePreference {
  const storage = browserStorage();
  const raw = storage?.getItem(STREAMING_SERVICE_PREFERENCE_KEY) ?? null;
  if (raw !== cachedRaw) {
    cachedRaw = raw;
    cachedPreference = readStreamingServicePreference(storage);
  }
  return cachedPreference;
}

function getServerSnapshot(): StreamingServicePreference {
  return EMPTY_PREFERENCE;
}

function browserStorage(): StorageLike | null {
  return typeof window === 'undefined' ? null : window.localStorage;
}
