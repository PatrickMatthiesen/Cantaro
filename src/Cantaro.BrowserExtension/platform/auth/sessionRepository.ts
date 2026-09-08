import { normalizeBaseUrl } from '../settings/extensionSettings';
import type { ExtensionSession } from './extensionSession';

export const LEGACY_SESSION_STORAGE_KEY = 'cantaro.session.v1';
export const PERSISTENT_SESSION_STORAGE_KEY = 'cantaro.refresh-session.v2';
export const ACCESS_SESSION_STORAGE_KEY = 'cantaro.access-session.v2';

interface StorageArea {
  get(key: string): Promise<Record<string, unknown>>;
  set(items: Record<string, unknown>): Promise<void>;
  remove(key: string): Promise<void>;
}

interface PersistentSession {
  baseUrl: string;
  refreshToken: string;
  email: string;
}

interface AccessSession {
  baseUrl: string;
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface SessionRepository {
  read(): Promise<ExtensionSession | null>;
  save(session: ExtensionSession): Promise<ExtensionSession>;
  clear(): Promise<void>;
}

function readString(record: Record<string, unknown>, key: string): string {
  return typeof record[key] === 'string' ? record[key].trim() : '';
}

function normalizeSession(session: ExtensionSession): ExtensionSession {
  return {
    baseUrl: normalizeBaseUrl(session.baseUrl),
    accessToken: session.accessToken.trim(),
    refreshToken: session.refreshToken.trim(),
    accessTokenExpiresAt: session.accessTokenExpiresAt.trim(),
    email: session.email.trim(),
  };
}

function record(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function parsePersistent(value: unknown): PersistentSession | null {
  const stored = record(value);
  if (!stored) return null;
  const session = {
    baseUrl: normalizeBaseUrl(readString(stored, 'baseUrl')),
    refreshToken: readString(stored, 'refreshToken'),
    email: readString(stored, 'email'),
  };
  return session.baseUrl && session.refreshToken ? session : null;
}

function parseAccess(value: unknown): AccessSession | null {
  const stored = record(value);
  if (!stored) return null;
  const session = {
    baseUrl: normalizeBaseUrl(readString(stored, 'baseUrl')),
    accessToken: readString(stored, 'accessToken'),
    accessTokenExpiresAt: readString(stored, 'accessTokenExpiresAt'),
  };
  return session.baseUrl && session.accessToken ? session : null;
}

function parseLegacy(value: unknown): ExtensionSession | null {
  const stored = record(value);
  if (!stored) return null;
  const session = normalizeSession({
    baseUrl: readString(stored, 'baseUrl'),
    accessToken: readString(stored, 'accessToken'),
    refreshToken: readString(stored, 'refreshToken'),
    accessTokenExpiresAt: readString(stored, 'accessTokenExpiresAt'),
    email: readString(stored, 'email'),
  });
  return session.baseUrl && session.refreshToken ? session : null;
}

export function createSessionRepository(
  persistentStorage: StorageArea,
  accessStorage: StorageArea,
): SessionRepository {
  const repository: SessionRepository = {
    async read() {
      const [persistentValues, accessValues] = await Promise.all([
        persistentStorage.get(PERSISTENT_SESSION_STORAGE_KEY),
        accessStorage.get(ACCESS_SESSION_STORAGE_KEY),
      ]);
      let persistent = parsePersistent(persistentValues[PERSISTENT_SESSION_STORAGE_KEY]);
      let access = parseAccess(accessValues[ACCESS_SESSION_STORAGE_KEY]);

      if (!persistent) {
        const legacyValues = await persistentStorage.get(LEGACY_SESSION_STORAGE_KEY);
        const legacy = parseLegacy(legacyValues[LEGACY_SESSION_STORAGE_KEY]);
        if (!legacy) return null;
        await repository.save(legacy);
        await persistentStorage.remove(LEGACY_SESSION_STORAGE_KEY);
        return legacy;
      }

      if (access && access.baseUrl !== persistent.baseUrl) {
        await accessStorage.remove(ACCESS_SESSION_STORAGE_KEY);
        access = null;
      }
      return {
        ...persistent,
        accessToken: access?.accessToken ?? '',
        accessTokenExpiresAt: access?.accessTokenExpiresAt ?? '',
      };
    },

    async save(session) {
      const normalized = normalizeSession(session);
      await persistentStorage.set({
        [PERSISTENT_SESSION_STORAGE_KEY]: {
          baseUrl: normalized.baseUrl,
          refreshToken: normalized.refreshToken,
          email: normalized.email,
        } satisfies PersistentSession,
      });
      if (normalized.accessToken) {
        await accessStorage.set({
          [ACCESS_SESSION_STORAGE_KEY]: {
            baseUrl: normalized.baseUrl,
            accessToken: normalized.accessToken,
            accessTokenExpiresAt: normalized.accessTokenExpiresAt,
          } satisfies AccessSession,
        });
      } else {
        await accessStorage.remove(ACCESS_SESSION_STORAGE_KEY);
      }
      await persistentStorage.remove(LEGACY_SESSION_STORAGE_KEY);
      return normalized;
    },

    async clear() {
      await Promise.all([
        persistentStorage.remove(PERSISTENT_SESSION_STORAGE_KEY),
        persistentStorage.remove(LEGACY_SESSION_STORAGE_KEY),
        accessStorage.remove(ACCESS_SESSION_STORAGE_KEY),
      ]);
    },
  };
  return repository;
}

export const browserSessionRepository = createSessionRepository(
  {
    get: (key) => browser.storage.local.get(key),
    set: (items) => browser.storage.local.set(items),
    remove: (key) => browser.storage.local.remove(key),
  },
  {
    get: (key) => browser.storage.session.get(key),
    set: (items) => browser.storage.session.set(items),
    remove: (key) => browser.storage.session.remove(key),
  },
);

/**
 * Watches only persistent session identity changes. Access-token rotation is
 * intentionally excluded so token refreshes cannot create notification loops.
 */
export function watchSessionIdentityChanges(listener: () => void): () => void {
  const readIdentity = (value: unknown): string => {
    const stored = record(value);
    if (!stored) return '';
    const baseUrl = normalizeBaseUrl(readString(stored, 'baseUrl'));
    const email = readString(stored, 'email').toLowerCase();
    return baseUrl && email ? `${baseUrl}\u0000${email}` : '';
  };

  const onChanged = (
    changes: Record<string, Browser.storage.StorageChange>,
    areaName: string,
  ) => {
    if (areaName !== 'local') return;
    // Legacy records are migrated by read() and removed immediately after the
    // v2 record is written. Watching that removal would create a duplicate
    // revocation and could race a newly saved consent record.
    const change = changes[PERSISTENT_SESSION_STORAGE_KEY];
    if (!change) return;
    if (readIdentity(change.oldValue) === readIdentity(change.newValue)) return;
    listener();
  };
  browser.storage.onChanged.addListener(onChanged);
  return () => browser.storage.onChanged.removeListener(onChanged);
}
