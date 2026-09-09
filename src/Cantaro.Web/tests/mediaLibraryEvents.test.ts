import { afterEach, describe, expect, mock, test } from 'bun:test';
import {
  configureMediaApi,
  mediaApi,
} from '../../Cantaro.ClientShared/src/media/services/mediaApi';
import { readLibraryEvents } from '../../Cantaro.ClientShared/src/media/services/mediaLibraryEventStream';
import { subscribeToMediaProgressUpdates } from '../../Cantaro.ClientShared/src/media/services/mediaProgressEvents';

const originalFetch = globalThis.fetch;
const originalWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
const originalDocument = Object.getOwnPropertyDescriptor(globalThis, 'document');
const originalSetTimeout = globalThis.setTimeout;
const originalClearTimeout = globalThis.clearTimeout;

const encoder = new TextEncoder();

function streamFromText(...chunks: string[]): ReadableStream<Uint8Array> {
  let index = 0;
  return new ReadableStream({
    pull(controller) {
      if (index === chunks.length) {
        controller.close();
        return;
      }
      controller.enqueue(encoder.encode(chunks[index++]));
    },
  });
}

function eventResponse(...chunks: string[]): Response {
  return new Response(streamFromText(...chunks), {
    headers: { 'content-type': 'text/event-stream' },
  });
}

async function flushPromises(): Promise<void> {
  await new Promise<void>((resolve) => originalSetTimeout(resolve, 0));
  for (let index = 0; index < 8; index += 1) await Promise.resolve();
}

function installDomStubs(): {
  window: { addEventListener: ReturnType<typeof mock>; removeEventListener: ReturnType<typeof mock> };
  document: {
    visibilityState: 'visible';
    addEventListener: ReturnType<typeof mock>;
    removeEventListener: ReturnType<typeof mock>;
  };
} {
  const windowStub = { addEventListener: mock(() => {}), removeEventListener: mock(() => {}) };
  const documentStub = {
    visibilityState: 'visible' as const,
    addEventListener: mock(() => {}),
    removeEventListener: mock(() => {}),
  };
  Object.defineProperty(globalThis, 'window', { configurable: true, value: windowStub });
  Object.defineProperty(globalThis, 'document', { configurable: true, value: documentStub });
  return { window: windowStub, document: documentStub };
}

function restoreGlobal(name: 'window' | 'document', descriptor: PropertyDescriptor | undefined): void {
  if (descriptor) {
    Object.defineProperty(globalThis, name, descriptor);
  } else {
    delete (globalThis as Record<string, unknown>)[name];
  }
}

afterEach(() => {
  globalThis.fetch = originalFetch;
  globalThis.setTimeout = originalSetTimeout;
  globalThis.clearTimeout = originalClearTimeout;
  configureMediaApi(() => ({ baseUrl: '', includeCredentials: true }));
  restoreGlobal('window', originalWindow);
  restoreGlobal('document', originalDocument);
});

describe('media library event stream', () => {
  test('parses CRLF frames split across arbitrary chunks', async () => {
    const payload = [
      'event: library-changed\r\n',
      'data: {"mediaTitleId":"title-1"}\r\n',
      '\r\n',
      'event: message\r\n',
      'data: {"mediaTitleId":"title-2"}\r\n',
      '\r\n',
    ].join('');
    const notifications: Array<{ mediaTitleId?: string }> = [];

    await readLibraryEvents(
      streamFromText(payload.slice(0, 9), payload.slice(9, 37), payload.slice(37)),
      (notification) => notifications.push(notification),
    );

    expect(notifications).toEqual([
      { mediaTitleId: 'title-1' },
      { mediaTitleId: 'title-2' },
    ]);
  });

  test('ignores heartbeats, malformed JSON, invalid payloads, and unknown events', async () => {
    const notifications: Array<{ mediaTitleId?: string }> = [];

    await readLibraryEvents(streamFromText([
      ': heartbeat\r\n',
      '\r\n',
      'event: library-changed\r\n',
      'data: {not-json}\r\n',
      '\r\n',
      'event: library-changed\r\n',
      'data: {"mediaTitleId":42}\r\n',
      '\r\n',
      'event: unrelated\r\n',
      'data: {"mediaTitleId":"ignored"}\r\n',
      '\r\n',
      'data: {"mediaTitleId":"accepted"}\r\n',
      '\r\n',
    ].join('')), (notification) => notifications.push(notification));

    expect(notifications).toEqual([{ mediaTitleId: 'accepted' }]);
  });
});

describe('media progress event subscription', () => {
  test('retries authentication failures so refreshed credentials can recover', async () => {
    installDomStubs();
    const timers: Array<() => void> = [];
    globalThis.setTimeout = ((callback: () => void) => {
      timers.push(callback);
      return timers.length;
    }) as typeof setTimeout;
    globalThis.clearTimeout = (() => {}) as typeof clearTimeout;
    let requests = 0;
    globalThis.fetch = mock(() => {
      requests++;
      return Promise.resolve(requests === 1
        ? new Response(null, { status: 401 })
        : eventResponse('event: library-changed\ndata: {}\n\n'));
    }) as typeof fetch;
    const listener = mock(() => {});
    const unsubscribe = subscribeToMediaProgressUpdates(listener);
    try {
      await flushPromises();
      expect(listener).not.toHaveBeenCalled();
      expect(timers).toHaveLength(1);
      timers[0]();
      await flushPromises();
      expect(listener).toHaveBeenCalledWith({});
    } finally {
      unsubscribe();
    }
  });

  test('shares one connection and aborts it after the final unsubscribe', async () => {
    const dom = installDomStubs();
    let requestSignal: AbortSignal | undefined;
    globalThis.fetch = mock((_input, init) => {
      requestSignal = init?.signal;
      const body = new ReadableStream<Uint8Array>({
        start(controller) {
          controller.enqueue(encoder.encode('data: {"mediaTitleId":"title-1"}\n\n'));
          requestSignal?.addEventListener('abort', () => controller.close());
        },
      });
      return Promise.resolve(new Response(body, {
        headers: { 'content-type': 'text/event-stream' },
      }));
    }) as typeof fetch;

    const firstListener = mock(() => {});
    const secondListener = mock(() => {});
    const unsubscribeFirst = subscribeToMediaProgressUpdates(firstListener);
    const unsubscribeSecond = subscribeToMediaProgressUpdates(secondListener);
    await flushPromises();

    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
    expect(firstListener).toHaveBeenCalledWith({ mediaTitleId: 'title-1' });
    expect(secondListener).toHaveBeenCalledWith({ mediaTitleId: 'title-1' });

    unsubscribeFirst();
    expect(requestSignal?.aborted).toBe(false);
    expect(dom.window.removeEventListener).not.toHaveBeenCalled();

    unsubscribeSecond();
    await flushPromises();
    expect(requestSignal?.aborted).toBe(true);
    expect(dom.window.removeEventListener).toHaveBeenCalledTimes(1);
    expect(dom.document.removeEventListener).toHaveBeenCalledTimes(1);
  });

  test('reconnects with fresh runtime credentials after a stream ends', async () => {
    installDomStubs();
    const timers: Array<(() => void) | undefined> = [];
    globalThis.setTimeout = ((callback: () => void) => {
      timers.push(callback);
      return timers.length;
    }) as typeof setTimeout;
    globalThis.clearTimeout = ((timer: ReturnType<typeof setTimeout>) => {
      timers[Number(timer) - 1] = undefined;
    }) as typeof clearTimeout;

    const requests: Array<{ url: string; headers: Headers; credentials: RequestCredentials }> = [];
    globalThis.fetch = mock((input, init) => {
      requests.push({
        url: String(input),
        headers: new Headers(init?.headers),
        credentials: init?.credentials ?? 'same-origin',
      });
      return Promise.resolve(eventResponse('data: {"mediaTitleId":"title-1"}\n\n'));
    }) as typeof fetch;
    configureMediaApi(() => ({
      baseUrl: 'https://cantaro.test/',
      accessToken: `token-${requests.length + 1}`,
      includeCredentials: true,
    }));

    const listener = mock(() => {});
    const unsubscribe = subscribeToMediaProgressUpdates(listener);
    await flushPromises();
    expect(requests).toHaveLength(1);
    expect(requests[0].headers.get('authorization')).toBe('Bearer token-1');
    expect(timers.filter(Boolean)).toHaveLength(1);

    timers.find(Boolean)?.();
    await flushPromises();
    expect(requests).toHaveLength(2);
    expect(requests[1].headers.get('authorization')).toBe('Bearer token-2');
    expect(listener).toHaveBeenCalledTimes(2);

    unsubscribe();
  });
});

describe('mediaApi.openLibraryEvents', () => {
  test('builds the authenticated event URL and SSE request headers', async () => {
    const controller = new AbortController();
    let requestedUrl = '';
    let requestedInit: RequestInit | undefined;
    globalThis.fetch = mock((input, init) => {
      requestedUrl = String(input);
      requestedInit = init;
      return Promise.resolve(new Response(null, {
        headers: { 'content-type': 'text/event-stream' },
      }));
    }) as typeof fetch;
    configureMediaApi(() => ({
      baseUrl: 'https://cantaro.test///',
      accessToken: 'access-token',
      includeCredentials: true,
    }));

    await mediaApi.openLibraryEvents(controller.signal);

    const headers = new Headers(requestedInit?.headers);
    expect(requestedUrl).toBe('https://cantaro.test/api/media/library/events');
    expect(requestedInit?.signal).toBe(controller.signal);
    expect(requestedInit?.credentials).toBe('omit');
    expect(requestedInit?.cache).toBe('no-store');
    expect(headers.get('accept')).toBe('text/event-stream');
    expect(headers.get('authorization')).toBe('Bearer access-token');
  });
});
