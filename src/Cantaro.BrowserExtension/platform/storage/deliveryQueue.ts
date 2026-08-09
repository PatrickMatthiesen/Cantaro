import type { SeriesCatalogObservation } from '../../features/media/contracts/catalogObservation';
import type { WatchProgressObservation } from '../../features/media/contracts/watchObservation';

export const DELIVERY_QUEUE_STORAGE_KEY = 'cantaro.deliveryQueue.v1';
const DEFAULT_MAX_SIZE = 50;

export type QueuedDelivery =
  | { kind: 'media.catalog'; payload: SeriesCatalogObservation }
  | { kind: 'media.watch'; payload: WatchProgressObservation };

export interface DeliveryQueueItem {
  id: string;
  delivery: QueuedDelivery;
  queuedAt: string;
  attempts: number;
}

interface StorageArea {
  get(key: string): Promise<Record<string, unknown>>;
  set(items: Record<string, unknown>): Promise<void>;
}

export interface DeliveryQueue {
  enqueue(delivery: QueuedDelivery): Promise<DeliveryQueueItem>;
  readBatch(limit: number): Promise<DeliveryQueueItem[]>;
  markSucceeded(ids: string[]): Promise<void>;
  markFailed(ids: string[]): Promise<void>;
}

function deliveryKey(delivery: QueuedDelivery): string {
  if (delivery.kind === 'media.catalog') {
    const value = delivery.payload;
    return `${delivery.kind}:${value.providerSeriesId}:${value.providerSeasonId ?? value.seasonNumber ?? ''}`;
  }
  return `${delivery.kind}:${delivery.payload.providerEpisodeId}`;
}

function parseItems(value: unknown): DeliveryQueueItem[] {
  return Array.isArray(value) ? value as DeliveryQueueItem[] : [];
}

export function createDeliveryQueue(
  storage: StorageArea,
  maxSize = DEFAULT_MAX_SIZE,
): DeliveryQueue {
  let mutation = Promise.resolve();
  const serialize = async <T>(operation: () => Promise<T>): Promise<T> => {
    const result = mutation.then(operation, operation);
    mutation = result.then(() => undefined, () => undefined);
    return result;
  };
  const save = (items: DeliveryQueueItem[]) => storage.set({ [DELIVERY_QUEUE_STORAGE_KEY]: items });
  const load = async () => parseItems(
    (await storage.get(DELIVERY_QUEUE_STORAGE_KEY))[DELIVERY_QUEUE_STORAGE_KEY],
  );

  return {
    enqueue: (delivery) => serialize(async () => {
      const items = await load();
      const key = deliveryKey(delivery);
      const existing = items.find((item) => deliveryKey(item.delivery) === key);
      if (existing) {
        existing.delivery = delivery;
        existing.queuedAt = new Date().toISOString();
        await save(items);
        return existing;
      }

      const item: DeliveryQueueItem = {
        id: crypto.randomUUID(),
        delivery,
        queuedAt: new Date().toISOString(),
        attempts: 0,
      };
      items.push(item);
      await save(items.slice(-maxSize));
      return item;
    }),

    readBatch: (limit) => serialize(async () => (await load()).slice(0, limit)),

    markSucceeded: (ids) => serialize(async () => {
      if (ids.length === 0) return;
      const succeeded = new Set(ids);
      await save((await load()).filter((item) => !succeeded.has(item.id)));
    }),

    markFailed: (ids) => serialize(async () => {
      if (ids.length === 0) return;
      const failed = new Set(ids);
      const items = await load();
      for (const item of items) {
        if (failed.has(item.id)) item.attempts += 1;
      }
      await save(items);
    }),
  };
}

export const browserDeliveryQueue = createDeliveryQueue({
  get: (key) => browser.storage.local.get(key),
  set: (items) => browser.storage.local.set(items),
});
