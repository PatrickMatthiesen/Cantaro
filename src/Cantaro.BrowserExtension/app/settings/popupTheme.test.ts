import { afterEach, expect, it, vi } from 'vitest';
import { restorePopupTheme } from './popupTheme';

afterEach(() => vi.unstubAllGlobals());

it.each([
  { cached: 'dark', systemDark: false, expected: 'dark' },
  { cached: 'light', systemDark: true, expected: 'light' },
  { cached: 'system', systemDark: true, expected: 'dark' },
  { cached: undefined, systemDark: false, expected: 'light' },
  { cached: undefined, systemDark: true, expected: 'dark' },
])('restores theme $expected on open with cache $cached and systemDark=$systemDark', async ({
  cached, systemDark, expected,
}) => {
  const dataset = {};
  const read = vi.fn().mockResolvedValue({ cantaroTheme: cached });
  vi.stubGlobal('browser', { storage: { local: { get: read } } });
  vi.stubGlobal('window', { matchMedia: () => ({ matches: systemDark }) });
  vi.stubGlobal('document', { documentElement: { dataset } });

  await restorePopupTheme();
  expect(read).toHaveBeenCalledExactlyOnceWith('cantaroTheme');
  expect(dataset).toEqual({ theme: expected });
});
