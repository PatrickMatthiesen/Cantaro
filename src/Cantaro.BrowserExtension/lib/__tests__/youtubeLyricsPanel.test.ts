import { describe, expect, it } from 'vitest';
import { parseHTML } from 'linkedom';
import { createLyricsPanel, findLyricsPanelMount, lyricsText, shouldRenderLyricsPanel, shouldResetLyricsPanel } from '../youtubeLyricsPanel';

function adapterFixture(elements: Record<string, unknown>) {
  return {
    querySelector(selector: string) {
      return (elements[selector] ?? null) as Element | null;
    },
  } as Pick<ParentNode, 'querySelector'>;
}

describe('YouTube lyrics panel adapter', () => {
  it('mounts on YouTube’s native sidebar when it is available', () => {
    const sidebar = { id: 'secondary-inner' } as unknown as HTMLElement;
    expect(findLyricsPanelMount(adapterFixture({ '#secondary-inner': sidebar }), 'youtube')).toEqual({ target: sidebar, layout: 'embedded' });
  });

  it('uses the YouTube Music player page as its native mount target', () => {
    const page = { id: 'player-page' } as unknown as HTMLElement;
    expect(findLyricsPanelMount(adapterFixture({ 'ytmusic-player-page': page }), 'youtube_music')).toEqual({ target: page, layout: 'embedded' });
  });

  it('does not create a panel for disabled, unmatched, or non-music pages', () => {
    expect(shouldRenderLyricsPanel(false, 'track-1', 'music')).toBe(false);
    expect(shouldRenderLyricsPanel(true, undefined, 'music')).toBe(false);
    expect(shouldRenderLyricsPanel(true, 'track-1', 'not_music')).toBe(false);
    expect(shouldRenderLyricsPanel(true, 'track-1', 'music')).toBe(true);
  });

  it('clears a prior render as soon as SPA navigation identifies a new track', () => {
    expect(shouldResetLyricsPanel('youtube:old-video:Old song', 'youtube:new-video:New song')).toBe(true);
    expect(shouldResetLyricsPanel('youtube:same-video:Same song', 'youtube:same-video:Same song')).toBe(false);
  });

  it('uses plain lyrics first and removes LRC timestamps when only synced lyrics are returned', () => {
    expect(lyricsText({ state: 'available', matchStatus: 'exact', provider: 'cantaro', attribution: 'Attribution', plainLyrics: 'A plain line', syncedLyrics: '[00:01.00]Timed line' })).toBe('A plain line');
    expect(lyricsText({ state: 'available', matchStatus: 'exact', provider: 'cantaro', attribution: 'Attribution', syncedLyrics: '[00:01.00]First\n[00:05.00]Second' })).toBe('First\nSecond');
  });

  it('does not resurrect a user-dismissed panel when the page DOM is restored', () => {
    const { document } = parseHTML('<html><body><aside id="first"></aside><aside id="second"></aside></body></html>');
    const panel = createLyricsPanel(document as unknown as Document);
    const first = document.getElementById('first') as unknown as HTMLElement;
    const second = document.getElementById('second') as unknown as HTMLElement;

    panel.mount({ target: first, layout: 'embedded' });
    panel.render({ trackLabel: 'Old song', loading: true });
    expect(first.querySelector('[data-cantaro-lyrics-panel]')).not.toBeNull();

    panel.dismiss();
    panel.mount({ target: second, layout: 'embedded' });
    panel.render({ trackLabel: 'Old song', loading: true });
    expect(document.querySelector('[data-cantaro-lyrics-panel]')).toBeNull();
  });

  it('uses a fresh panel for an SPA track transition after tearing down the prior panel', () => {
    const { document } = parseHTML('<html><body><aside id="old"></aside><aside id="next"></aside></body></html>');
    const oldTarget = document.getElementById('old') as unknown as HTMLElement;
    const nextTarget = document.getElementById('next') as unknown as HTMLElement;
    const oldPanel = createLyricsPanel(document as unknown as Document);

    oldPanel.mount({ target: oldTarget, layout: 'embedded' });
    oldPanel.render({ trackLabel: 'Old song', loading: true });
    oldPanel.destroy();
    const nextPanel = createLyricsPanel(document as unknown as Document);
    nextPanel.mount({ target: nextTarget, layout: 'embedded' });
    nextPanel.render({ trackLabel: 'New song', loading: true });

    expect(oldTarget.querySelector('[data-cantaro-lyrics-panel]')).toBeNull();
    expect(nextTarget.querySelector('[data-cantaro-lyrics-panel]')).not.toBeNull();
  });
});
