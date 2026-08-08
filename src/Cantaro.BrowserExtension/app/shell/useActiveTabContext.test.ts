import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createActiveTabContextRefresher,
  subscribeToActiveTabContextChanges,
} from './useActiveTabContext';
import type { ActiveTabContextState, ExtensionAppServices } from './extensionAppTypes';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('subscribeToActiveTabContextChanges', () => {
  it('refreshes for tab-local context notifications and removes every listener', () => {
    let runtimeListener: ((message: unknown) => unknown) | undefined;
    let activatedListener: (() => void) | undefined;
    let updatedListener: ((tabId: number, change: { status?: string; url?: string }) => void) | undefined;
    const removeRuntime = vi.fn();
    const removeActivated = vi.fn();
    const removeUpdated = vi.fn();
    vi.stubGlobal('browser', {
      runtime: { onMessage: { addListener: vi.fn(listener => { runtimeListener = listener; }), removeListener: removeRuntime } },
      tabs: {
        onActivated: { addListener: vi.fn(listener => { activatedListener = listener; }), removeListener: removeActivated },
        onUpdated: { addListener: vi.fn(listener => { updatedListener = listener; }), removeListener: removeUpdated },
      },
    } as unknown as typeof browser);
    const refresh = vi.fn();
    const refreshContext = vi.fn();

    const stop = subscribeToActiveTabContextChanges(refresh, refreshContext);
    runtimeListener?.({ type: 'unrelated' });
    runtimeListener?.({ type: 'tab.context.changed' });
    activatedListener?.();
    updatedListener?.(1, { status: 'loading' });
    updatedListener?.(1, { url: 'https://www.crunchyroll.com/watch/NEXT' });
    expect(refresh).toHaveBeenCalledTimes(2);
    expect(refreshContext).toHaveBeenCalledTimes(1);

    stop();
    expect(removeRuntime).toHaveBeenCalledWith(runtimeListener);
    expect(removeActivated).toHaveBeenCalledWith(activatedListener);
    expect(removeUpdated).toHaveBeenCalledWith(updatedListener);
  });

  it('ignores a slower response after a newer active-tab refresh finishes', async () => {
    let resolveFirst: ((value: Awaited<ReturnType<ExtensionAppServices['readActiveTabContext']>>) => void) | undefined;
    const first = new Promise<Awaited<ReturnType<ExtensionAppServices['readActiveTabContext']>>>(resolve => {
      resolveFirst = resolve;
    });
    const second = Promise.resolve({
      feature: 'media' as const,
      provider: 'crunchyroll' as const,
      pageKind: 'series' as const,
      status: 'ready' as const,
      pageUrl: 'https://www.crunchyroll.com/series/SECOND/show',
      providerSeriesId: 'SECOND',
      observedEpisodeCount: 12,
    });
    const services: ExtensionAppServices = {
      readActiveTabContext: vi.fn()
        .mockReturnValueOnce(first)
        .mockReturnValueOnce(second),
    };
    const states: ActiveTabContextState[] = [];
    const refresh = createActiveTabContextRefresher(services, state => states.push(state));

    const olderRefresh = refresh();
    await refresh();
    resolveFirst?.({
      feature: 'media',
      provider: 'crunchyroll',
      pageKind: 'series',
      status: 'ready',
      pageUrl: 'https://www.crunchyroll.com/series/FIRST/show',
      providerSeriesId: 'FIRST',
      observedEpisodeCount: 1,
    });
    await olderRefresh;

    expect(states.at(-1)).toMatchObject({
      status: 'available',
      snapshot: { providerSeriesId: 'SECOND' },
    });
  });
});
