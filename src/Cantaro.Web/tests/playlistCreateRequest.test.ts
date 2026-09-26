import { expect, test } from 'bun:test';
import type { PlaylistCreatePreview } from '@cantaro/client-shared/music';
import { PlaylistCreateRequestGate, previewAndCreatePlaylist } from '../src/music/playlistCreateRequest';

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}

test('same-name candidates require a choice before any creation', async () => {
  const gate = new PlaylistCreateRequestGate();
  const preview: PlaylistCreatePreview = {
    candidates: [{ servicePlaylistId: 'remote', name: 'Coding', remoteTrackCount: 10,
      sharedTrackCount: 8, linkedPlaylistId: null, comparisonError: null }],
    previewToken: 'review-first',
  };
  let creates = 0;
  const request = gate.begin();
  expect(request).not.toBeNull();
  const result = await previewAndCreatePlaylist(gate, request!, async () => preview,
    async () => { creates++; return true; }, () => {});

  expect(result).toEqual({ kind: 'candidates', preview });
  expect(creates).toBe(0);
});

test('closing during preview prevents playlist creation', async () => {
  const gate = new PlaylistCreateRequestGate();
  const preview = deferred<PlaylistCreatePreview>();
  let creates = 0;
  const request = gate.begin();
  expect(request).not.toBeNull();
  const pending = previewAndCreatePlaylist(gate, request!, () => preview.promise, async () => { creates++; return true; }, () => {});

  gate.cancel();
  preview.resolve({ candidates: [], previewToken: 'reviewed' });

  expect(await pending).toEqual({ kind: 'canceled' });
  expect(creates).toBe(0);
});

test('rapid duplicate requests submit one creation with its review token', async () => {
  const gate = new PlaylistCreateRequestGate();
  const preview = deferred<PlaylistCreatePreview>();
  const tokens: string[] = [];
  const create = async (token: string) => { tokens.push(token); return true; };
  const request = gate.begin();
  expect(request).not.toBeNull();
  const first = previewAndCreatePlaylist(gate, request!, () => preview.promise, create, () => {});
  const duplicate = gate.begin();

  expect(duplicate).toBeNull();
  preview.resolve({ candidates: [], previewToken: 'only-once' });
  expect(await first).toEqual({ kind: 'created', succeeded: true });
  expect(tokens).toEqual(['only-once']);
});
