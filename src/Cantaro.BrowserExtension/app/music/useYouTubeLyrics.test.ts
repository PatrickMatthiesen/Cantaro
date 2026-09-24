import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { parseHTML } from 'linkedom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConsentStatus } from '../../platform/consent/consentService';
import type { CurrentYouTubeState } from './useYouTubeLyrics';
import { useYouTubeLyrics } from './useYouTubeLyrics';

const mocks = vi.hoisted(() => ({
  getStatus: vi.fn(), subscribe: vi.fn(), requestLyrics: vi.fn(),
}));
vi.mock('../../platform/consent/runtimeConsentClient', () => ({
  getRuntimeConsentStatus: mocks.getStatus,
  subscribeRuntimeConsent: mocks.subscribe,
}));
vi.mock('../../features/music/youtube/youtubeLyrics', () => ({
  youtubeVideoId: (url: string) => new URL(url).searchParams.get('v'),
  requestYouTubeLyrics: mocks.requestLyrics,
}));

const denied: ConsentStatus = { consentVersion: 1, needsReview: false, authenticated: true,
  watchTrackingAllowed: false, catalogCollectionAllowed: false, musicLyricsAllowed: false };
const allowed: ConsentStatus = { ...denied, musicLyricsAllowed: true };
let root: Root;
let current: CurrentYouTubeState;
let notifyConsent: (status: ConsentStatus) => void;
let notifyUpdated: (tabId: number, change: { status?: string; url?: string }) => void;
let activeUrl = 'https://www.youtube.com/watch?v=abcdefghijk';

function Harness({ enabled = true }: { enabled?: boolean }) {
  current = useYouTubeLyrics(enabled);
  return null;
}

beforeEach(() => {
  vi.resetAllMocks();
  const dom = parseHTML('<html><body><div id="root"></div></body></html>');
  vi.stubGlobal('window', dom.window);
  vi.stubGlobal('document', dom.document);
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  activeUrl = 'https://www.youtube.com/watch?v=abcdefghijk';
  const listener = { addListener: vi.fn(), removeListener: vi.fn() };
  listener.addListener.mockImplementation((callback: typeof notifyUpdated) => { notifyUpdated = callback; });
  const activated = { addListener: vi.fn(), removeListener: vi.fn() };
  const queried = vi.fn(async () => [{ id: 7, url: activeUrl }]);
  vi.stubGlobal('browser', { tabs: { query: queried, onActivated: activated, onUpdated: listener } });
  root = createRoot(dom.document.getElementById('root') as unknown as HTMLElement);
  mocks.getStatus.mockResolvedValue(allowed);
  mocks.subscribe.mockImplementation((callback: typeof notifyConsent) => {
    notifyConsent = callback;
    return vi.fn();
  });
  mocks.requestLyrics.mockResolvedValue({ song: { id: 'song-1', title: 'Song' }, lyrics: null });
});

afterEach(async () => { await act(async () => root.unmount()); vi.unstubAllGlobals(); });

describe('current YouTube lyrics hook', () => {
  it('does not inspect tabs until authenticated lyrics consent is enabled', async () => {
    mocks.getStatus.mockResolvedValue(denied);
    await act(async () => root.render(createElement(Harness)));
    await act(async () => Promise.resolve());
    expect(current.kind).toBe('disabled');
    expect(mocks.requestLyrics).not.toHaveBeenCalled();
  });

  it('clears immediately on consent revocation and ignores an older response', async () => {
    let finishLookup: (value: { song: { id: string; title: string }; lyrics: null }) => void = () => {};
    mocks.requestLyrics.mockImplementation(() => new Promise(resolve => { finishLookup = resolve; }));
    await act(async () => root.render(createElement(Harness)));
    await act(async () => Promise.resolve());
    expect(current.kind).toBe('loading');
    await act(async () => notifyConsent(denied));
    expect(current.kind).toBe('disabled');
    await act(async () => finishLookup({ song: { id: 'stale', title: 'Stale' }, lyrics: null }));
    expect(current.kind).toBe('disabled');
  });

  it('ignores a lookup from the previous video after active tab navigation', async () => {
    const completions: Array<(value: { song: { id: string; title: string }; lyrics: null }) => void> = [];
    mocks.requestLyrics.mockImplementation(() => new Promise(resolve => { completions.push(resolve); }));
    await act(async () => root.render(createElement(Harness)));
    await act(async () => Promise.resolve());
    activeUrl = 'https://www.youtube.com/watch?v=zyxwvutsrqp';
    await act(async () => notifyUpdated(7, { url: activeUrl }));
    expect(completions).toHaveLength(2);
    await act(async () => completions[0]!({ song: { id: 'old', title: 'Old song' }, lyrics: null }));
    expect(current.kind).toBe('loading');
    await act(async () => completions[1]!({ song: { id: 'new', title: 'New song' }, lyrics: null }));
    expect(current.kind).toBe('song');
    if (current.kind === 'song') expect(current.song.title).toBe('New song');
  });
});
