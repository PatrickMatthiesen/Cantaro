import { afterEach, describe, expect, it, vi } from 'vitest';
import { parseHTML } from 'linkedom';
import { hasRelevantPageChange, logWatchProgressStatus } from './watchController';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('Crunchyroll watch progress logging', () => {
  it('logs threshold reached at info exactly once without duplicating the verbose progress log', () => {
    const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);
    const verbose = vi.fn();

    logWatchProgressStatus({
      type: 'threshold-reached',
      watchProgressPercent: 85,
      positionSeconds: 1_275,
      durationSeconds: 1_500,
    }, 'GE00340376ENUS', verbose);

    expect(info).toHaveBeenCalledOnce();
    expect(info).toHaveBeenCalledWith('Cantaro: Crunchyroll watch threshold reached', {
      watchId: 'GE00340376ENUS',
      type: 'threshold-reached',
      watchProgressPercent: 85,
      positionSeconds: 1_275,
      durationSeconds: 1_500,
    });
    expect(verbose).not.toHaveBeenCalled();
  });

  it('keeps ordinary progress verbose-only', () => {
    const info = vi.spyOn(console, 'info').mockImplementation(() => undefined);
    const verbose = vi.fn();

    logWatchProgressStatus({
      type: 'progress',
      watchProgressPercent: 42,
      positionSeconds: 630,
      durationSeconds: 1_500,
    }, 'GE00340376ENUS', verbose);

    expect(info).not.toHaveBeenCalled();
    expect(verbose).toHaveBeenCalledOnce();
  });
});

describe('Crunchyroll watch page changes', () => {
  it.each([
    '<h1>E24 - A Story of a Dream with No End</h1>',
    '<a href="/series/GW4HM7WK9/wistoria-wand-and-sword">Wistoria: Wand and Sword</a>',
    '<section><video></video></section>',
  ])('retries tracking when current Crunchyroll metadata renders: %s', (html) => {
    const { document, window } = parseHTML('<html><body></body></html>');
    vi.stubGlobal('Element', window.Element);
    const host = document.createElement('div');
    host.innerHTML = html;

    expect(hasRelevantPageChange({ addedNodes: host.childNodes } as Pick<MutationRecord, 'addedNodes'>))
      .toBe(true);
  });

  it('ignores unrelated page mutations', () => {
    const { document, window } = parseHTML('<html><body></body></html>');
    vi.stubGlobal('Element', window.Element);
    const node = document.createElement('div');
    node.textContent = 'Unrelated footer update';

    expect(hasRelevantPageChange({ addedNodes: [node] } as unknown as Pick<MutationRecord, 'addedNodes'>))
      .toBe(false);
  });
});
