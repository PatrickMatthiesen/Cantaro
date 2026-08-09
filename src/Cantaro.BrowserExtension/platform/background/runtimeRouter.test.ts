import { describe, expect, it, vi } from 'vitest';
import type { MediaRequestHandler } from '../../features/media/background/mediaRequestHandler';
import { createRuntimeRouter } from './runtimeRouter';

const mediaHandler: MediaRequestHandler = {
  handle: vi.fn(),
  drainQueue: vi.fn(async () => ({ delivered: 2, remaining: 1 })),
};

describe('runtimeRouter', () => {
  it('routes explicit queue-drain requests', async () => {
    const result = await createRuntimeRouter(mediaHandler)({
      type: 'delivery.queue.drain',
      correlationId: 'correlation',
    }, {});

    expect(result).toEqual({
      ok: true,
      value: { delivered: 2, remaining: 1 },
      correlationId: 'correlation',
    });
  });

  it('ignores messages owned by another extension feature', async () => {
    expect(await createRuntimeRouter(mediaHandler)({ type: 'unknown' }, {}))
      .toBeUndefined();
  });
});
