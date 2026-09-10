import { mediaApi } from './mediaApi';
import { readLibraryEvents, type MediaProgressUpdateNotification } from './mediaLibraryEventStream';

const listeners = new Set<(notification: MediaProgressUpdateNotification) => void>();
let stopConnection: (() => void) | undefined;

function isEventStream(response: Response): boolean {
  return response.ok && response.body !== null
    && response.headers.get('content-type')?.includes('text/event-stream') === true;
}

function notifyListeners(notification: MediaProgressUpdateNotification, signal: AbortSignal) {
  if (signal.aborted) return;
  for (const listener of listeners) listener(notification);
}

function connect(): () => void {
  let stopped = false;
  let controller: AbortController | undefined;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let retryDelay = 1_000;

  const run = async () => {
    controller = new AbortController();
    const activeController = controller;
    try {
      const response = await mediaApi.openLibraryEvents(activeController.signal);
      if (!isEventStream(response)) {
        await response.body?.cancel();
        return;
      }
      await readLibraryEvents(response.body!, (notification) => {
        retryDelay = 1_000;
        notifyListeners(notification, activeController.signal);
      });
    } catch {
      // Reconnect with fresh runtime credentials after network/server interruptions.
    } finally {
      activeController.abort();
      if (!stopped && controller === activeController) {
        retryTimer = setTimeout(() => { void run(); }, retryDelay);
        retryDelay = Math.min(retryDelay * 2, 30_000);
      }
    }
  };

  const resume = () => {
    if (document.visibilityState === 'hidden') return;
    clearTimeout(retryTimer);
    controller?.abort();
    void run();
  };
  window.addEventListener('online', resume);
  document.addEventListener('visibilitychange', resume);
  void run();

  return () => {
    stopped = true;
    clearTimeout(retryTimer);
    controller?.abort();
    window.removeEventListener('online', resume);
    document.removeEventListener('visibilitychange', resume);
  };
}

export function subscribeToMediaProgressUpdates(
  listener: (notification: MediaProgressUpdateNotification) => void,
): () => void {
  listeners.add(listener);
  stopConnection ??= connect();
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      stopConnection?.();
      stopConnection = undefined;
    }
  };
}
