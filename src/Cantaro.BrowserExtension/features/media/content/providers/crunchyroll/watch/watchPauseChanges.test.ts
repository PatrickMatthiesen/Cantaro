import { afterEach, describe, expect, it, vi } from 'vitest';
import { watchTrackingPauseChanges } from './watchPauseChanges';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('watchTrackingPauseChanges', () => {
  it('reacts to the versioned pause preference and unsubscribes', () => {
    let storageListener: ((changes: Record<string, unknown>, area: string) => void) | undefined;
    const removeListener = vi.fn();
    vi.stubGlobal('browser', {
      storage: {
        onChanged: {
          addListener: vi.fn(listener => { storageListener = listener; }),
          removeListener,
        },
      },
    } as unknown as typeof browser);
    const changed = vi.fn();
    const stop = watchTrackingPauseChanges(changed);

    storageListener?.({ unrelated: {} }, 'local');
    storageListener?.({ 'cantaro.media.trackingPause.v1': {} }, 'local');
    storageListener?.({ 'cantaro.media.trackingPause.v1': {} }, 'sync');
    expect(changed).toHaveBeenCalledTimes(1);

    stop();
    expect(removeListener).toHaveBeenCalledWith(storageListener);
  });
});
