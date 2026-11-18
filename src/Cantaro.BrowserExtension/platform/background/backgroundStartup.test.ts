import { afterEach, expect, it, vi } from 'vitest';

const route = vi.hoisted(() => vi.fn());
vi.mock('./runtimeRouter', () => ({ runtimeRouter: route }));

afterEach(() => vi.unstubAllGlobals());

it('registers the waking message and waits for storage protection before routing it', async () => {
  const storageReady = Promise.withResolvers<void>();
  const addListener = vi.fn<typeof browser.runtime.onMessage.addListener>();
  vi.stubGlobal('browser', {
    runtime: { onMessage: { addListener } },
    storage: {
      local: {
        setAccessLevel: vi.fn(() => storageReady.promise),
        remove: vi.fn().mockRejectedValue(new Error('Legacy cleanup failed')),
      },
      onChanged: { addListener: vi.fn() },
    },
  });
  vi.stubGlobal('defineBackground', (main: () => void) => { main(); });

  await import('../../entrypoints/background');
  expect(addListener).toHaveBeenCalledTimes(1);
  const message = { type: 'consent.status', correlationId: 'first-open', payload: {} };
  const sender = {};
  const reply = { ok: true };
  route.mockResolvedValue(reply);
  const receive = addListener.mock.calls[0]![0];
  const response = Promise.withResolvers<unknown>();
  const sendResponse = vi.fn(response.resolve);
  expect(receive({ type: 'tab.context.changed' }, sender, sendResponse)).toBeUndefined();
  expect(receive(message, sender, sendResponse)).toBe(true);
  expect(route).not.toHaveBeenCalled();
  expect(sendResponse).not.toHaveBeenCalled();

  storageReady.resolve();
  await expect(response.promise).resolves.toEqual(reply);
  expect(sendResponse).toHaveBeenCalledExactlyOnceWith(reply);
  expect(route).toHaveBeenCalledExactlyOnceWith(message, sender);
});
