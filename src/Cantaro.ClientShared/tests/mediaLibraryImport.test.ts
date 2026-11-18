import { expect, mock, spyOn, test } from 'bun:test';
import { MediaApiClient } from '../src/media/services/mediaApi.client';
import { MediaApiError } from '../src/media/services/mediaApi.errors';
import type { MediaLibraryImportEventDto } from '../src/media/services/mediaApi.types';

function importEvent(status: string): MediaLibraryImportEventDto {
  return {
    providerId: 'myanimelist', importId: 'import-id', status, importedCount: 0,
    createdTitles: 0, createdEntries: 0, updatedEntries: 0,
    libraryChanged: false, occurredAt: '2026-11-18T18:00:00Z',
  };
}

function createImport() {
  const client = new MediaApiClient();
  const close = mock();
  const source = { close } as unknown as EventSource;
  const subscription = Promise.withResolvers<{
    event: (event: MediaLibraryImportEventDto) => void;
    disconnect: () => void;
  }>();
  client.importLibrary = async () => ({
    providerId: 'myanimelist', importId: 'import-id', status: 'queued',
  });
  client.subscribeToImportEvents = async (_providerId, _importId, event, onError) => {
    subscription.resolve({ event, disconnect: () => onError?.() });
    return source;
  };
  return { client, close, subscribed: subscription.promise };
}

test('a paused import stops waiting and points the user to reconnect', async () => {
  const { client, close, subscribed } = createImport();
  const work = client.importLibraryAndWait('myanimelist');
  const { event } = await subscribed;
  event(importEvent('queued'));
  event(importEvent('reconnect_required'));

  await expect(work).rejects.toThrow('Reconnect MyAnimeList in Settings');
  expect(close).toHaveBeenCalledTimes(1);
});

test('an already expired connection does not open a refresh stream', async () => {
  const { client } = createImport();
  const subscribe = spyOn(client, 'subscribeToImportEvents');
  client.importLibrary = async () => {
    throw new MediaApiError('request', 'Failed to import media library', { code: 'platform_reconnect_required' });
  };

  await expect(client.importLibraryAndWait('myanimelist')).rejects.toThrow('Reconnect MyAnimeList in Settings');
  expect(subscribe).not.toHaveBeenCalled();
});

test('a completed import returns its result and closes the stream', async () => {
  const { client, close, subscribed } = createImport();
  const work = client.importLibraryAndWait('myanimelist');
  const { event } = await subscribed;
  const completed = importEvent('completed');
  event(completed);

  await expect(work).resolves.toBe(completed);
  expect(close).toHaveBeenCalledTimes(1);
});

test('a disconnected import stream stops waiting', async () => {
  const { client, close, subscribed } = createImport();
  const work = client.importLibraryAndWait('myanimelist');
  const { disconnect } = await subscribed;
  disconnect();

  await expect(work).rejects.toThrow("Can't connect to the Cantaro server");
  expect(close).toHaveBeenCalledTimes(1);
});

test('cancelling a refresh closes a late subscription', async () => {
  const { client } = createImport();
  const started = Promise.withResolvers<void>();
  const opened = Promise.withResolvers<EventSource>();
  const close = mock();
  client.subscribeToImportEvents = () => {
    started.resolve();
    return opened.promise;
  };
  const refresh = new AbortController();
  const work = client.importLibraryAndWait('myanimelist', refresh.signal);
  await started.promise;
  refresh.abort();

  await expect(work).rejects.toMatchObject({ name: 'AbortError' });
  opened.resolve({ close } as unknown as EventSource);
  await opened.promise;
  expect(close).toHaveBeenCalledTimes(1);
});

test('an import that never publishes completion stops waiting after the timeout', async () => {
  const { client, close, subscribed } = createImport();
  const schedule = globalThis.setTimeout;
  const timer = spyOn(globalThis, 'setTimeout').mockImplementationOnce((callback) => schedule(callback, 0));
  try {
    const work = client.importLibraryAndWait('myanimelist');
    await subscribed;
    await expect(work).rejects.toThrow('Reload is taking longer than expected');
    expect(close).toHaveBeenCalledTimes(1);
  } finally {
    timer.mockRestore();
  }
});
