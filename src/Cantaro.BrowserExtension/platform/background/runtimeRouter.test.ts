import { describe, expect, it, vi } from 'vitest';
import type { MediaRequestHandler } from '../../features/media/background/mediaRequestHandler';
import { createRuntimeRouter } from './runtimeRouter';

const mediaHandler: MediaRequestHandler = {
  handle: vi.fn(),
};

describe('runtimeRouter', () => {
  it('ignores messages owned by another extension feature', async () => {
    expect(await createRuntimeRouter(mediaHandler)({ type: 'unknown' }, {}))
      .toBeUndefined();
  });
});
