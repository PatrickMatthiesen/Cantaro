import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { parseHTML } from 'linkedom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { LyricsResultContent } from './LyricsResultContent';
import type { LyricsResult } from './musicLyrics';

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

it('defaults to the best lyrics and switches locally, including instrumental versions', async () => {
  const best: LyricsResult = { state: 'available', matchStatus: 'fallback', provider: 'lrclib', providerRecordId: '1', plainLyrics: 'Synthetic first version', attribution: 'LRCLIB' };
  const result: LyricsResult = { ...best, candidates: [
    { ...best, label: 'Album A · 3:53' },
    { ...best, providerRecordId: '2', label: 'Album B · 3:57', plainLyrics: 'Synthetic alternate version' },
    { ...best, providerRecordId: '3', label: 'Instrumental', state: 'instrumental', plainLyrics: undefined },
  ] };
  await act(async () => root.render(createElement(LyricsResultContent, { result })));
  expect(container.textContent).toContain('Synthetic first version');
  const select = container.querySelector('select')!;
  Object.defineProperty(select, 'value', { configurable: true, value: '1' });
  await act(async () => select.dispatchEvent(new window.Event('change', { bubbles: true })));
  expect(container.textContent).toContain('Synthetic alternate version');
  expect(container.textContent).not.toContain('Synthetic first version');
  Object.defineProperty(select, 'value', { configurable: true, value: '2' });
  await act(async () => select.dispatchEvent(new window.Event('change', { bubbles: true })));
  expect(container.textContent).toContain('Instrumental track');
  expect(container.textContent).not.toContain('Synthetic alternate version');
  await act(async () => root.render(createElement(LyricsResultContent, { result: best })));
  expect(container.querySelector('select')).toBeNull();
  expect(container.textContent).toContain('Synthetic first version');
});
