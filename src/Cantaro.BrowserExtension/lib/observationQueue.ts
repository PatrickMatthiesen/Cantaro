import type { MediaObservation } from './mediaObservation';

const QUEUE_KEY = 'cantaro_observation_queue';

/** Maximum number of pending observations kept in local storage. */
const DEFAULT_MAX_SIZE = 50;

export interface QueuedObservation {
  id: string;
  observation: MediaObservation;
  queuedAt: string;
  /** Number of attempted deliveries that have failed. */
  attempts: number;
}

/** Minimal storage interface so the queue can be unit-tested without browser APIs. */
export interface StorageAdapter {
  get(key: string): Promise<unknown>;
  set(key: string, value: unknown): Promise<void>;
}

export interface ObservationQueue {
  /** Append an observation. Drops the oldest entry when the queue is full. */
  enqueue(observation: MediaObservation): Promise<void>;
  /** Return all queued items without removing them. */
  drain(): Promise<QueuedObservation[]>;
  /** Remove successfully-delivered items by their ids. */
  remove(ids: string[]): Promise<void>;
  /** Record a failed delivery attempt for the given item ids. */
  incrementAttempts(ids: string[]): Promise<void>;
}

function generateId(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;
}

/**
 * Create a bounded offline queue backed by the provided storage adapter.
 * The queue caps at `maxSize` entries; when full, the oldest entry is dropped
 * to make room, preventing unbounded growth.
 */
export function createObservationQueue(
  storage: StorageAdapter,
  maxSize: number = DEFAULT_MAX_SIZE,
): ObservationQueue {
  async function load(): Promise<QueuedObservation[]> {
    const raw = await storage.get(QUEUE_KEY);
    if (!Array.isArray(raw)) return [];
    return raw as QueuedObservation[];
  }

  async function save(items: QueuedObservation[]): Promise<void> {
    await storage.set(QUEUE_KEY, items);
  }

  return {
    async enqueue(observation: MediaObservation): Promise<void> {
      const items = await load();
      if (items.length >= maxSize) {
        items.shift(); // drop oldest when at capacity
      }
      items.push({
        id: generateId(),
        observation,
        queuedAt: new Date().toISOString(),
        attempts: 0,
      });
      await save(items);
    },

    async drain(): Promise<QueuedObservation[]> {
      return load();
    },

    async remove(ids: string[]): Promise<void> {
      if (ids.length === 0) return;
      const idSet = new Set(ids);
      const items = await load();
      await save(items.filter((item) => !idSet.has(item.id)));
    },

    async incrementAttempts(ids: string[]): Promise<void> {
      if (ids.length === 0) return;
      const idSet = new Set(ids);
      const items = await load();
      for (const item of items) {
        if (idSet.has(item.id)) {
          item.attempts += 1;
        }
      }
      await save(items);
    },
  };
}

/** Pre-built queue instance wired to the extension's local storage. */
export function createBrowserStorageQueue(): ObservationQueue {
  return createObservationQueue({
    get: async (key: string) => {
      const result = await browser.storage.local.get(key);
      return result[key];
    },
    set: async (key: string, value: unknown) => {
      await browser.storage.local.set({ [key]: value });
    },
  });
}
