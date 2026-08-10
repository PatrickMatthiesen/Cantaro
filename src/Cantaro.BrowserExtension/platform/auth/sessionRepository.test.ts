import { describe, expect, it } from 'vitest';
import { createSessionRepository, SESSION_STORAGE_KEY } from './sessionRepository';

function memoryStorage(initial: Record<string, unknown> = {}) {
  const values = { ...initial };
  return {
    values,
    async get(keys: string | string[]) {
      const requested = Array.isArray(keys) ? keys : [keys];
      return Object.fromEntries(requested.map((key) => [key, values[key]]));
    },
    async set(items: Record<string, unknown>) { Object.assign(values, items); },
    async remove(keys: string | string[]) {
      for (const key of Array.isArray(keys) ? keys : [keys]) delete values[key];
    },
  };
}

describe('sessionRepository', () => {
  it('reads the versioned session record', async () => {
    const storage = memoryStorage({
      [SESSION_STORAGE_KEY]: {
        baseUrl: 'https://api.example.test/',
        accessToken: 'access',
        refreshToken: 'refresh',
        accessTokenExpiresAt: '2030-01-01T00:00:00.000Z',
        email: 'user@example.test',
      },
    });

    const session = await createSessionRepository(storage).read();

    expect(session?.email).toBe('user@example.test');
  });

  it('clears only the versioned session record', async () => {
    const storage = memoryStorage({
      [SESSION_STORAGE_KEY]: { accessToken: 'access' },
    });
    await createSessionRepository(storage).clear();
    expect(storage.values[SESSION_STORAGE_KEY]).toBeUndefined();
  });

  it('does not read flat session fields', async () => {
    const storage = memoryStorage({
      baseUrl: 'https://api.example.test',
      accessToken: 'flat-access',
      refreshToken: 'flat-refresh',
      accessTokenExpiresAt: '2030-01-01T00:00:00.000Z',
      sessionEmail: 'user@example.test',
    });

    await expect(createSessionRepository(storage).read()).resolves.toBeNull();
  });
});
