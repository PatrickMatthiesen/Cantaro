import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ACCESS_SESSION_STORAGE_KEY,
  createSessionRepository,
  LEGACY_SESSION_STORAGE_KEY,
  PERSISTENT_SESSION_STORAGE_KEY,
  watchSessionIdentityChanges,
} from './sessionRepository';

function memoryStorage(initial: Record<string, unknown> = {}) {
  const values = { ...initial };
  return {
    values,
    async get(key: string) { return { [key]: values[key] }; },
    async set(items: Record<string, unknown>) { Object.assign(values, items); },
    async remove(key: string) { delete values[key]; },
  };
}

const fullSession = {
  baseUrl: 'https://api.example.test/',
  accessToken: 'access',
  refreshToken: 'refresh',
  accessTokenExpiresAt: '2030-01-01T00:00:00.000Z',
  email: 'user@example.test',
};

describe('sessionRepository', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('splits persistent refresh state from the session access token', async () => {
    const persistent = memoryStorage();
    const access = memoryStorage();
    const repository = createSessionRepository(persistent, access);

    await repository.save(fullSession);

    expect(persistent.values[PERSISTENT_SESSION_STORAGE_KEY]).toEqual({
      baseUrl: 'https://api.example.test',
      refreshToken: 'refresh',
      email: 'user@example.test',
    });
    expect(persistent.values[PERSISTENT_SESSION_STORAGE_KEY]).not.toHaveProperty('accessToken');
    expect(access.values[ACCESS_SESSION_STORAGE_KEY]).toEqual({
      baseUrl: 'https://api.example.test',
      accessToken: 'access',
      accessTokenExpiresAt: '2030-01-01T00:00:00.000Z',
    });
    await expect(repository.read()).resolves.toEqual({
      ...fullSession,
      baseUrl: 'https://api.example.test',
    });
  });

  it('preserves login across restart without persisting the access token', async () => {
    const persistent = memoryStorage({
      [PERSISTENT_SESSION_STORAGE_KEY]: {
        baseUrl: 'https://api.example.test',
        refreshToken: 'refresh',
        email: 'user@example.test',
      },
    });
    const repository = createSessionRepository(persistent, memoryStorage());

    await expect(repository.read()).resolves.toEqual({
      baseUrl: 'https://api.example.test',
      refreshToken: 'refresh',
      email: 'user@example.test',
      accessToken: '',
      accessTokenExpiresAt: '',
    });
  });

  it('migrates the combined v1 record into split storage', async () => {
    const persistent = memoryStorage({ [LEGACY_SESSION_STORAGE_KEY]: fullSession });
    const access = memoryStorage();
    const repository = createSessionRepository(persistent, access);

    await expect(repository.read()).resolves.toEqual({
      ...fullSession,
      baseUrl: 'https://api.example.test',
    });
    expect(persistent.values[LEGACY_SESSION_STORAGE_KEY]).toBeUndefined();
    expect(persistent.values[PERSISTENT_SESSION_STORAGE_KEY]).toBeDefined();
    expect(access.values[ACCESS_SESSION_STORAGE_KEY]).toBeDefined();
  });

  it('discards an access token belonging to another server', async () => {
    const persistent = memoryStorage({
      [PERSISTENT_SESSION_STORAGE_KEY]: {
        baseUrl: 'https://api.example.test', refreshToken: 'refresh', email: 'user@example.test',
      },
    });
    const access = memoryStorage({
      [ACCESS_SESSION_STORAGE_KEY]: {
        baseUrl: 'https://other.example.test', accessToken: 'wrong', accessTokenExpiresAt: '2030-01-01T00:00:00.000Z',
      },
    });

    const session = await createSessionRepository(persistent, access).read();

    expect(session?.accessToken).toBe('');
    expect(access.values[ACCESS_SESSION_STORAGE_KEY]).toBeUndefined();
  });

  it('clears persistent, session, and legacy credentials', async () => {
    const persistent = memoryStorage({
      [PERSISTENT_SESSION_STORAGE_KEY]: {}, [LEGACY_SESSION_STORAGE_KEY]: {}, unrelated: true,
    });
    const access = memoryStorage({ [ACCESS_SESSION_STORAGE_KEY]: {}, unrelated: true });

    await createSessionRepository(persistent, access).clear();

    expect(persistent.values).toEqual({ unrelated: true });
    expect(access.values).toEqual({ unrelated: true });
  });

  it('notifies only when the persistent session identity changes', () => {
    let storageListener: ((changes: Record<string, unknown>, areaName: string) => void) | undefined;
    const removeListener = vi.fn();
    vi.stubGlobal('browser', {
      storage: { onChanged: {
        addListener: (listener: typeof storageListener) => { storageListener = listener; },
        removeListener,
      } },
    });
    const changed = vi.fn();
    const stop = watchSessionIdentityChanges(changed);
    storageListener?.({ [ACCESS_SESSION_STORAGE_KEY]: { newValue: { accessToken: 'rotated' } } }, 'session');
    expect(changed).not.toHaveBeenCalled();
    const identity = { baseUrl: 'https://api.example.test', email: 'user@example.test', refreshToken: 'one' };
    storageListener?.({ [PERSISTENT_SESSION_STORAGE_KEY]: { newValue: identity } }, 'local');
    storageListener?.({ [PERSISTENT_SESSION_STORAGE_KEY]: { oldValue: identity, newValue: { ...identity, refreshToken: 'two' } } }, 'local');
    expect(changed).toHaveBeenCalledOnce();
    storageListener?.({ [PERSISTENT_SESSION_STORAGE_KEY]: { oldValue: identity, newValue: undefined } }, 'local');
    expect(changed).toHaveBeenCalledTimes(2);
    stop();
    expect(removeListener).toHaveBeenCalledOnce();
  });
});
