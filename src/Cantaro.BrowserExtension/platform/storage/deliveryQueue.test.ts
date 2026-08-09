import { describe, expect, it } from 'vitest';
import { createDeliveryQueue, DELIVERY_QUEUE_STORAGE_KEY } from './deliveryQueue';
import type { SeriesCatalogObservation } from '../../features/media/contracts/catalogObservation';

function catalog(episodeCount: number): SeriesCatalogObservation {
  return {
    schemaVersion: 1,
    provider: 'crunchyroll',
    providerSeriesId: 'SERIES',
    seriesTitle: 'Series',
    seasonTitle: 'Season 1',
    seasonNumber: 1,
    seriesUrl: 'https://www.crunchyroll.com/series/SERIES/series',
    episodes: Array.from({ length: episodeCount }, (_, index) => ({
      providerEpisodeId: `EPISODE${index + 1}`,
      providerUrl: `https://www.crunchyroll.com/watch/EPISODE${index + 1}/episode`,
      episodeNumber: index + 1,
    })),
    observedAt: '2026-08-08T00:00:00.000Z',
    extensionVersion: '0.1.0',
  };
}

function memoryStorage(initial: Record<string, unknown> = {}) {
  const values: Record<string, unknown> = { ...initial };
  return {
    values,
    async get(key: string) { return { [key]: values[key] }; },
    async set(items: Record<string, unknown>) { Object.assign(values, items); },
  };
}

describe('deliveryQueue', () => {
  it('coalesces repeated catalog evidence for the same series and season', async () => {
    const storage = memoryStorage();
    const queue = createDeliveryQueue(storage);

    await Promise.all([
      queue.enqueue({ kind: 'media.catalog', payload: catalog(1) }),
      queue.enqueue({ kind: 'media.catalog', payload: catalog(3) }),
    ]);

    const items = await queue.readBatch(10);
    expect(items).toHaveLength(1);
    expect(items[0].delivery.kind).toBe('media.catalog');
    if (items[0].delivery.kind === 'media.catalog') {
      expect(items[0].delivery.payload.episodes).toHaveLength(3);
    }
    expect(storage.values[DELIVERY_QUEUE_STORAGE_KEY]).toHaveLength(1);
  });

  it('serializes successful removal and failed-attempt updates', async () => {
    const storage = memoryStorage();
    const queue = createDeliveryQueue(storage);
    const first = await queue.enqueue({ kind: 'media.catalog', payload: catalog(1) });
    const second = await queue.enqueue({
      kind: 'media.catalog',
      payload: { ...catalog(2), seasonNumber: 2, seasonTitle: 'Season 2' },
    });

    await Promise.all([queue.markSucceeded([first.id]), queue.markFailed([second.id])]);

    const items = await queue.readBatch(10);
    expect(items).toHaveLength(1);
    expect(items[0].id).toBe(second.id);
    expect(items[0].attempts).toBe(1);
  });

});
