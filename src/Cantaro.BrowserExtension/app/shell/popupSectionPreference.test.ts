import { afterEach, describe, expect, it, vi } from 'vitest';
import { readPreferredSection, rememberPreferredSection } from './popupSectionPreference';

afterEach(() => vi.unstubAllGlobals());

function stubStorage(stored: Record<string, unknown> = {}) {
  const set = vi.fn(async () => undefined);
  vi.stubGlobal('browser', {
    storage: {
      local: {
        get: vi.fn(async () => stored),
        set,
      },
    },
  });
  return { set };
}

describe('popup section preference', () => {
  it('reads the versioned popup section preference', async () => {
    const storage = stubStorage({ 'cantaro.popup.section.v1': 'media' });
    await expect(readPreferredSection()).resolves.toBe('media');
    expect(storage.set).not.toHaveBeenCalled();
  });

  it('stores only primary application sections', async () => {
    const storage = stubStorage();
    await rememberPreferredSection('music');
    expect(storage.set).toHaveBeenCalledWith({ 'cantaro.popup.section.v1': 'music' });
  });
});
