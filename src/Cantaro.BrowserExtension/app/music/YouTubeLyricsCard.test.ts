import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { parseHTML } from 'linkedom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CurrentYouTubeState } from './useYouTubeLyrics';
import { YouTubeLyricsCard } from './YouTubeLyricsCard';

const mock = vi.hoisted(() => ({ state: { kind: 'disabled' } as CurrentYouTubeState }));
vi.mock('./useYouTubeLyrics', () => ({ useYouTubeLyrics: () => mock.state }));

let root: Root;
let container: HTMLElement;

beforeEach(() => {
  const dom = parseHTML('<html><body><div id="root"></div></body></html>');
  vi.stubGlobal('window', dom.window);
  vi.stubGlobal('document', dom.document);
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = dom.document.getElementById('root') as unknown as HTMLElement;
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  vi.unstubAllGlobals();
});

describe('YouTube lyrics card', () => {
  it('stays hidden until consent enables lyrics', async () => {
    mock.state = { kind: 'disabled' };
    await act(async () => root.render(createElement(YouTubeLyricsCard, { enabled: true })));
    expect(container.textContent).toBe('');
  });

  it('shows a clear unmatched state without affecting the library page', async () => {
    mock.state = { kind: 'unmatched', videoId: 'abcdefghijk' };
    await act(async () => root.render(createElement(YouTubeLyricsCard, { enabled: true })));
    expect(container.textContent).toContain('This video is not linked to a song in your Cantaro library.');
  });

  it('shows the matched song and collapsible lyrics', async () => {
    mock.state = {
      kind: 'song', videoId: 'abcdefghijk', song: { id: 'track-1', title: 'A song', artist: 'An artist' },
      lyrics: { state: 'available', matchStatus: 'exact', provider: 'LRCLIB', attribution: 'Lyrics provided by LRCLIB', plainLyrics: 'First line\nSecond line' },
    };
    await act(async () => root.render(createElement(YouTubeLyricsCard, { enabled: true })));
    expect(container.textContent).toContain('A song');
    expect(container.textContent).toContain('An artist');
    const details = container.querySelector('details');
    expect(details?.hasAttribute('open')).toBe(false);
    expect(details?.querySelector('summary')?.textContent).toBe('Show lyrics');
    expect(details?.textContent).toContain('First line\nSecond line');

    details?.setAttribute('open', '');
    mock.state = { ...mock.state, videoId: 'zyxwvutsrqp' };
    await act(async () => root.render(createElement(YouTubeLyricsCard, { enabled: true })));
    expect(container.querySelector('details')?.hasAttribute('open')).toBe(false);
  });
});
