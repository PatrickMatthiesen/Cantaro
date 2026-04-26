import { describe, it, expect, beforeEach } from 'vitest';
import { createObservationQueue } from '../observationQueue';
import type { StorageAdapter } from '../observationQueue';
import type { MediaObservation } from '../mediaObservation';
import { SiteIds, EXTENSION_VERSION } from '../mediaObservation';

// ---------------------------------------------------------------------------
// In-memory storage adapter for tests
// ---------------------------------------------------------------------------

function makeMemoryStorage(): StorageAdapter & { _store: Record<string, unknown> } {
  const _store: Record<string, unknown> = {};
  return {
    _store,
    async get(key: string) {
      return _store[key];
    },
    async set(key: string, value: unknown) {
      _store[key] = value;
    },
  };
}

function makeObservation(overrides: Partial<MediaObservation> = {}): MediaObservation {
  return {
    siteId: SiteIds.Crunchyroll,
    observedUrl: 'https://www.crunchyroll.com/watch/GYVNM7N6Y/episode-1',
    siteMediaId: 'GYVNM7N6Y',
    titleText: 'Episode 1 - My Anime',
    progressHint: 1,
    observedAt: new Date().toISOString(),
    extensionVersion: EXTENSION_VERSION,
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// enqueue / drain
// ---------------------------------------------------------------------------

describe('observationQueue — enqueue and drain', () => {
  let storage: ReturnType<typeof makeMemoryStorage>;
  let queue: ReturnType<typeof createObservationQueue>;

  beforeEach(() => {
    storage = makeMemoryStorage();
    queue = createObservationQueue(storage);
  });

  it('starts empty', async () => {
    const items = await queue.drain();
    expect(items).toHaveLength(0);
  });

  it('enqueues a single observation', async () => {
    await queue.enqueue(makeObservation());
    const items = await queue.drain();
    expect(items).toHaveLength(1);
    expect(items[0].observation.siteId).toBe(SiteIds.Crunchyroll);
    expect(items[0].attempts).toBe(0);
    expect(items[0].id).toMatch(/^\d+-[a-z0-9]+$/);
  });

  it('preserves insertion order across multiple enqueues', async () => {
    for (let i = 1; i <= 3; i++) {
      await queue.enqueue(makeObservation({ titleText: `Episode ${i}` }));
    }
    const items = await queue.drain();
    expect(items.map((item) => item.observation.titleText)).toEqual([
      'Episode 1',
      'Episode 2',
      'Episode 3',
    ]);
  });
});

// ---------------------------------------------------------------------------
// Bounded growth
// ---------------------------------------------------------------------------

describe('observationQueue — bounded growth', () => {
  it('drops oldest entry when queue is full', async () => {
    const storage = makeMemoryStorage();
    const queue = createObservationQueue(storage, 3 /* maxSize */);

    await queue.enqueue(makeObservation({ titleText: 'First' }));
    await queue.enqueue(makeObservation({ titleText: 'Second' }));
    await queue.enqueue(makeObservation({ titleText: 'Third' }));
    // At capacity; next enqueue must drop "First"
    await queue.enqueue(makeObservation({ titleText: 'Fourth' }));

    const items = await queue.drain();
    expect(items).toHaveLength(3);
    expect(items.map((i) => i.observation.titleText)).toEqual(['Second', 'Third', 'Fourth']);
  });

  it('never grows beyond maxSize', async () => {
    const maxSize = 5;
    const storage = makeMemoryStorage();
    const queue = createObservationQueue(storage, maxSize);

    for (let i = 0; i < 20; i++) {
      await queue.enqueue(makeObservation({ titleText: `Episode ${i}` }));
    }

    const items = await queue.drain();
    expect(items).toHaveLength(maxSize);
  });
});

// ---------------------------------------------------------------------------
// remove
// ---------------------------------------------------------------------------

describe('observationQueue — remove', () => {
  it('removes items by id', async () => {
    const storage = makeMemoryStorage();
    const queue = createObservationQueue(storage);

    await queue.enqueue(makeObservation({ titleText: 'A' }));
    await queue.enqueue(makeObservation({ titleText: 'B' }));
    await queue.enqueue(makeObservation({ titleText: 'C' }));

    const [a, b] = await queue.drain();
    await queue.remove([a.id, b.id]);

    const remaining = await queue.drain();
    expect(remaining).toHaveLength(1);
    expect(remaining[0].observation.titleText).toBe('C');
  });

  it('is a no-op when given an empty id list', async () => {
    const storage = makeMemoryStorage();
    const queue = createObservationQueue(storage);
    await queue.enqueue(makeObservation());

    await queue.remove([]);

    expect(await queue.drain()).toHaveLength(1);
  });

  it('ignores unknown ids', async () => {
    const storage = makeMemoryStorage();
    const queue = createObservationQueue(storage);
    await queue.enqueue(makeObservation());

    await queue.remove(['nonexistent-id']);

    expect(await queue.drain()).toHaveLength(1);
  });
});

// ---------------------------------------------------------------------------
// incrementAttempts
// ---------------------------------------------------------------------------

describe('observationQueue — incrementAttempts', () => {
  it('increments attempt count on specified items', async () => {
    const storage = makeMemoryStorage();
    const queue = createObservationQueue(storage);
    await queue.enqueue(makeObservation({ titleText: 'A' }));
    await queue.enqueue(makeObservation({ titleText: 'B' }));

    const [a] = await queue.drain();
    await queue.incrementAttempts([a.id]);
    await queue.incrementAttempts([a.id]);

    const items = await queue.drain();
    const updatedA = items.find((i) => i.id === a.id)!;
    const updatedB = items.find((i) => i.observation.titleText === 'B')!;

    expect(updatedA.attempts).toBe(2);
    expect(updatedB.attempts).toBe(0);
  });

  it('is a no-op for an empty id list', async () => {
    const storage = makeMemoryStorage();
    const queue = createObservationQueue(storage);
    await queue.enqueue(makeObservation());

    await queue.incrementAttempts([]);
    const [item] = await queue.drain();
    expect(item.attempts).toBe(0);
  });
});
