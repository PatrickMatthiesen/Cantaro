import { parseHTML } from 'linkedom';
import { describe, expect, it, vi } from 'vitest';
import type { LyricsCandidate } from '../../../app/music/musicLyrics';
import { mountYouTubeLyricsDrawer } from './drawer';
import type { YouTubeLyricsResponse } from './youtubeLyrics';

function response(plainLyrics: string, candidates?: LyricsCandidate[]): YouTubeLyricsResponse {
  return {
    song: { id: 'song-1', title: 'Sample song', artist: 'Sample artist' },
    lyrics: {
      state: 'available', matchStatus: 'exact', provider: 'test', plainLyrics, attribution: 'Test provider', candidates,
    },
  };
}

function candidate(plainLyrics: string, label: string, overrides: Partial<LyricsCandidate> = {}): LyricsCandidate {
  return {
    state: 'available', matchStatus: 'exact', provider: 'test', providerRecordId: label,
    plainLyrics, attribution: `Attribution ${label}`, label, ...overrides,
  };
}

function getDrawerRoot(document: Document): ShadowRoot {
  const host = document.querySelector('[data-cantaro-youtube-lyrics]');
  if (!host?.shadowRoot) throw new Error('Expected the preview drawer to use an open shadow root.');
  return host.shadowRoot;
}

describe('YouTube lyrics drawer', () => {
  it('waits until opened, displays lyrics as text, and closes with Escape', async () => {
    const { document, window } = parseHTML('<html><body></body></html>');
    const requestLyrics = vi.fn(async () => response('<script>unsafe</script>\nSecond line'));
    const drawer = mountYouTubeLyricsDrawer({
      document: document as unknown as Document,
      shadowMode: 'open',
      getCurrentVideoId: () => 'abcdefghijk',
      requestLyrics,
    });
    const root = getDrawerRoot(document as unknown as Document);
    const launcher = root.querySelector('button[aria-label="Cantaro lyrics"]')!;

    expect(requestLyrics).not.toHaveBeenCalled();
    launcher.dispatchEvent(new window.Event('click', { bubbles: true }));
    await Promise.resolve();
    await Promise.resolve();

    expect(requestLyrics).toHaveBeenCalledOnce();
    expect(root.querySelector('pre')?.textContent).toBe('<script>unsafe</script>\nSecond line');
    expect(root.querySelector('pre script')).toBeNull();
    root.querySelector('button[aria-label="Search lyrics"]')!.dispatchEvent(new window.Event('click'));
    expect((root.querySelector('input[aria-label="Song title"]') as HTMLInputElement).value).toBe('Sample song');
    const artist = root.querySelector('input[aria-label="Artist"]') as HTMLInputElement;
    expect(artist.value).toBe('Sample artist');
    artist.value = 'My correction';
    root.querySelector('button[aria-label="Search lyrics"]')!.dispatchEvent(new window.Event('click'));
    root.querySelector('button[aria-label="Search lyrics"]')!.dispatchEvent(new window.Event('click'));
    expect(artist.value).toBe('My correction');
    artist.value = '';
    root.querySelector('button[aria-label="Search lyrics"]')!.dispatchEvent(new window.Event('click'));
    root.querySelector('button[aria-label="Search lyrics"]')!.dispatchEvent(new window.Event('click'));
    expect(artist.value).toBe('');


    const close = root.querySelector('button[aria-label="Close lyrics"]')!;
    const escape = new window.Event('keydown', { bubbles: true, composed: true });
    Object.defineProperty(escape, 'key', { value: 'Escape' });
    close.dispatchEvent(escape);
    expect(root.querySelector('section')?.hasAttribute('open')).toBe(false);
    expect(launcher.getAttribute('aria-expanded')).toBe('false');
    drawer.dispose();
    expect(document.querySelector('[data-cantaro-youtube-lyrics]')).toBeNull();
  });

  it('ignores an older response after the video changes', async () => {
    const { document, window } = parseHTML('<html><body></body></html>');
    let videoId = 'abcdefghijk';
    let resolveFirst!: (value: YouTubeLyricsResponse) => void;
    const requestLyrics = vi.fn((id: string) => id === 'abcdefghijk'
      ? new Promise<YouTubeLyricsResponse>(resolve => { resolveFirst = resolve; })
      : Promise.resolve(response('Current video lyrics')));
    const drawer = mountYouTubeLyricsDrawer({
      document: document as unknown as Document,
      shadowMode: 'open',
      getCurrentVideoId: () => videoId,
      requestLyrics,
    });
    const root = getDrawerRoot(document as unknown as Document);
    root.querySelector('button[aria-label="Cantaro lyrics"]')!.dispatchEvent(new window.Event('click', { bubbles: true }));
    videoId = 'lmnopqrstuv';
    drawer.refreshForCurrentVideo();
    await Promise.resolve();
    resolveFirst(response('Stale video lyrics'));
    await Promise.resolve();

    expect(root.querySelector('pre')?.textContent).toBe('Current video lyrics');
    expect(requestLyrics).toHaveBeenCalledTimes(2);
    drawer.dispose();
  });

  it('shows the best lyrics by default and switches versions locally', async () => {
    const { document, window } = parseHTML('<html><body></body></html>');
    const best = candidate('Best version lyrics', 'Studio version');
    const alternate = candidate('Alternate lyrics', 'Live version');
    const requestLyrics = vi.fn(async () => response(best.plainLyrics!, [best, alternate]));
    const drawer = mountYouTubeLyricsDrawer({
      document: document as unknown as Document,
      shadowMode: 'open',
      getCurrentVideoId: () => 'abcdefghijk',
      requestLyrics,
    });
    const root = getDrawerRoot(document as unknown as Document);
    root.querySelector('button[aria-label="Cantaro lyrics"]')!.dispatchEvent(new window.Event('click', { bubbles: true }));
    await Promise.resolve();
    await Promise.resolve();

    const select = root.querySelector<HTMLSelectElement>('select[aria-label="Lyrics version"]')!;
    expect(select.value).toBe('0');
    expect(root.querySelector('pre')?.textContent).toBe('Best version lyrics');
    expect(Array.from(select.querySelectorAll('option')).map(option => option.textContent)).toEqual(['Studio version', 'Live version']);

    Object.defineProperty(select, 'value', { configurable: true, value: '1' });
    select.dispatchEvent(new window.Event('change', { bubbles: true }));
    expect(root.querySelector('pre')?.textContent).toBe('Alternate lyrics');
    expect(root.querySelector('.attribution')?.textContent).toBe('Attribution Live version');
    expect(requestLyrics).toHaveBeenCalledOnce();
    drawer.dispose();
  });

  it('lets the user choose instrumental alternatives and resets to the best result on a new video', async () => {
    const { document, window } = parseHTML('<html><body></body></html>');
    let videoId = 'abcdefghijk';
    const best = candidate('Best version lyrics', 'Studio version');
    const instrumental = candidate('', 'Instrumental version', { state: 'instrumental', explanation: 'No vocal track.' });
    const requestLyrics = vi.fn(async (id: string) => id === 'abcdefghijk'
      ? response(best.plainLyrics!, [best, instrumental])
      : response('New video best lyrics', [candidate('New video best lyrics', 'Best match'), candidate('New video alternate', 'Alternate')]));
    const drawer = mountYouTubeLyricsDrawer({
      document: document as unknown as Document,
      shadowMode: 'open',
      getCurrentVideoId: () => videoId,
      requestLyrics,
    });
    const root = getDrawerRoot(document as unknown as Document);
    root.querySelector('button[aria-label="Cantaro lyrics"]')!.dispatchEvent(new window.Event('click', { bubbles: true }));
    await Promise.resolve();
    await Promise.resolve();

    const select = root.querySelector('select[aria-label="Lyrics version"]')!;
    Object.defineProperty(select, 'value', { configurable: true, value: '1' });
    select.dispatchEvent(new window.Event('change', { bubbles: true }));
    expect(root.querySelector('.notice-title')?.textContent).toBe('Instrumental track');
    expect(root.querySelector('.notice-detail')?.textContent).toBe('No vocal track.');

    videoId = 'lmnopqrstuv';
    drawer.refreshForCurrentVideo();
    await Promise.resolve();
    await Promise.resolve();
    expect(root.querySelector('pre')?.textContent).toBe('New video best lyrics');
    expect((root.querySelector('select[aria-label="Lyrics version"]') as HTMLSelectElement).value).toBe('0');
    drawer.dispose();
  });
  it.each(['Singer', ''])('searches manually when the video is not linked with artist %s', async (artistName) => {
    const { document, window } = parseHTML('<html><body></body></html>');
    const requestLyrics = vi.fn(async (_id: string, search?: { title: string; artist: string }) => search
      ? response('Manually found lyrics') : { song: null, lyrics: null });
    const drawer = mountYouTubeLyricsDrawer({ document: document as unknown as Document, shadowMode: 'open', getCurrentVideoId: () => 'abcdefghijk', requestLyrics });
    const root = getDrawerRoot(document as unknown as Document);
    root.querySelector('button[aria-label="Cantaro lyrics"]')!.dispatchEvent(new window.Event('click'));
    await Promise.resolve();
    await Promise.resolve();
    const form = root.querySelector('form')!;
    expect(form.hidden).toBe(false);
    (root.querySelector('input[aria-label="Song title"]') as HTMLInputElement).value = '  Example  ';
    (root.querySelector('input[aria-label="Artist"]') as HTMLInputElement).value = artistName;
    form.dispatchEvent(new window.Event('submit', { cancelable: true }));
    await Promise.resolve();
    await Promise.resolve();
    expect(requestLyrics).toHaveBeenLastCalledWith('abcdefghijk', { title: 'Example', artist: artistName });
    expect(root.querySelector('pre')?.textContent).toBe('Manually found lyrics');
    drawer.dispose();
  });

  it('repeats the manual search when the drawer is reopened', async () => {
    const { document, window } = parseHTML('<html><body></body></html>');
    const requestLyrics = vi.fn(async (_id: string, search?: { title: string; artist: string }) => search
      ? response(`Lyrics for ${search.title}`) : { song: null, lyrics: null });
    const drawer = mountYouTubeLyricsDrawer({ document: document as unknown as Document, shadowMode: 'open', getCurrentVideoId: () => 'abcdefghijk', requestLyrics });
    const root = getDrawerRoot(document as unknown as Document);
    const launcher = root.querySelector('button[aria-label="Cantaro lyrics"]')!;
    launcher.dispatchEvent(new window.Event('click'));
    await Promise.resolve();
    await Promise.resolve();
    (root.querySelector('input[aria-label="Song title"]') as HTMLInputElement).value = 'Manual song';
    (root.querySelector('input[aria-label="Artist"]') as HTMLInputElement).value = '';
    root.querySelector('form')!.dispatchEvent(new window.Event('submit', { cancelable: true }));
    await Promise.resolve();
    await Promise.resolve();
    expect(root.querySelector('pre')?.textContent).toBe('Lyrics for Manual song');

    root.querySelector('button[aria-label="Close lyrics"]')!.dispatchEvent(new window.Event('click'));
    launcher.dispatchEvent(new window.Event('click'));
    await Promise.resolve();
    await Promise.resolve();

    expect(requestLyrics).toHaveBeenLastCalledWith('abcdefghijk', { title: 'Manual song', artist: '' });
    expect(root.querySelector('pre')?.textContent).toBe('Lyrics for Manual song');
    drawer.dispose();
  });

  it('moves independently of resizing, stops on release, and stays in the viewport', () => {
    const { document, window } = parseHTML('<html><body></body></html>');
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1000 });
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 });
    const drawer = mountYouTubeLyricsDrawer({ document: document as unknown as Document, shadowMode: 'open', getCurrentVideoId: () => 'abcdefghijk', requestLyrics: async () => response('Words') });
    const root = getDrawerRoot(document as unknown as Document);
    const host = document.querySelector('[data-cantaro-youtube-lyrics]') as unknown as HTMLElement;
    const pointer = (type: string, x: number, y: number) => {
      const event = new window.Event(type, { bubbles: true, cancelable: true });
      Object.assign(event, { button: 0, clientX: x, clientY: y });
      return event;
    };
    root.querySelector('.heading')!.dispatchEvent(pointer('pointerdown', 800, 400));
    window.dispatchEvent(pointer('pointermove', 700, 300));
    expect(host.style.right).toBe('116px');
    expect(host.style.bottom).toBe('116px');
    expect((root.querySelector('.panel') as HTMLElement).style.width).toBe('380px');
    window.dispatchEvent(pointer('pointerup', 700, 300));
    window.dispatchEvent(pointer('pointermove', 500, 200));
    expect(host.style.right).toBe('116px');
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 420 });
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 560 });
    window.dispatchEvent(new window.Event('resize'));
    expect(host.style.right).toBe('24px');
    expect(host.style.bottom).toBe('24px');
    drawer.dispose();
  });

});
