import { afterEach, describe, expect, it, vi } from 'vitest';
import { parseHTML } from 'linkedom';
import { createLyricsPanel } from './lyricsPanel';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('YouTube lyrics panel ownership', () => {
  it('namespaces the injected host by the extension runtime ID', () => {
    const first = parseHTML('<html><body></body></html>');
    vi.stubGlobal('browser', { runtime: { id: 'abcdefghijklmnop' } });
    const firstPanel = createLyricsPanel(first.document);
    firstPanel.mount({ target: first.document.body, layout: 'embedded' });

    const second = parseHTML('<html><body></body></html>');
    vi.stubGlobal('browser', { runtime: { id: 'ponmlkjihgfedcba' } });
    const secondPanel = createLyricsPanel(second.document);
    secondPanel.mount({ target: second.document.body, layout: 'embedded' });

    const firstHost = first.document.body.firstElementChild;
    const secondHost = second.document.body.firstElementChild;
    expect(firstHost?.id).toMatch(/^cantaro-prod-[a-z0-9]{13}-lyrics-panel$/);
    expect(secondHost?.id).toMatch(/^cantaro-prod-[a-z0-9]{13}-lyrics-panel$/);
    expect(firstHost?.id).not.toBe(secondHost?.id);
    expect(firstHost?.getAttribute('data-cantaro-owner')).toBe(firstHost?.getAttribute('data-cantaro-lyrics-panel'));
    expect(secondHost?.getAttribute('data-cantaro-owner')).toBe(secondHost?.getAttribute('data-cantaro-lyrics-panel'));

    firstPanel.destroy();
    secondPanel.destroy();
  });
});
