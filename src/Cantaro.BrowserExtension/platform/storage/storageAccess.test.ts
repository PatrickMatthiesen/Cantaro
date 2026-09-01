import { describe, expect, it, vi } from 'vitest';
import { restrictLocalStorageToTrustedContexts } from './storageAccess';

describe('restrictLocalStorageToTrustedContexts', () => {
  it('prevents content scripts from reading local extension storage', async () => {
    const setAccessLevel = vi.fn(async () => {});

    await restrictLocalStorageToTrustedContexts({ setAccessLevel });

    expect(setAccessLevel).toHaveBeenCalledWith({ accessLevel: 'TRUSTED_CONTEXTS' });
  });

  it('allows browsers without storage access-level support', async () => {
    await expect(restrictLocalStorageToTrustedContexts({})).resolves.toBeUndefined();
  });
});
