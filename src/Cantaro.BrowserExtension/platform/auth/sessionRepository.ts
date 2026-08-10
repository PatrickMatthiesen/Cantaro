import { normalizeBaseUrl } from '../settings/extensionSettings';
import type { ExtensionSession } from './extensionSession';

export const SESSION_STORAGE_KEY = 'cantaro.session.v1';

interface StorageArea {
  get(key: string): Promise<Record<string, unknown>>;
  set(items: Record<string, unknown>): Promise<void>;
  remove(key: string): Promise<void>;
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

function parseSession(value: unknown): ExtensionSession | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const record = value as Record<string, unknown>;
  const session = normalizeSession({
    baseUrl: readString(record, 'baseUrl'),
    accessToken: readString(record, 'accessToken'),
    refreshToken: readString(record, 'refreshToken'),
    accessTokenExpiresAt: readString(record, 'accessTokenExpiresAt'),
    email: readString(record, 'email'),
  });
  return session.accessToken || session.refreshToken ? session : null;
}

export function createSessionRepository(storage: StorageArea): SessionRepository {
  return {
    async read() {
      const stored = await storage.get(SESSION_STORAGE_KEY);
      return parseSession(stored[SESSION_STORAGE_KEY]);
    },

    async save(session) {
      const normalized = normalizeSession(session);
      await storage.set({ [SESSION_STORAGE_KEY]: normalized });
      return normalized;
    },

    async clear() {
      await storage.remove(SESSION_STORAGE_KEY);
    },
  };
}

export const browserSessionRepository = createSessionRepository({
  get: (key) => browser.storage.local.get(key),
  set: (items) => browser.storage.local.set(items),
  remove: (key) => browser.storage.local.remove(key),
});
