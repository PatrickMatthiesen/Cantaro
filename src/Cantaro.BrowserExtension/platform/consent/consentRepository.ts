import { normalizeBaseUrl } from '../settings/extensionSettings';

export const CONSENT_STORAGE_KEY = 'cantaro.consent.v2';
export const LEGACY_CONSENT_STORAGE_KEY = 'cantaro.consent.v1';
export const CURRENT_CONSENT_VERSION = 4;

export interface ConsentChoices {
  watchTracking: boolean;
  catalogCollection: boolean;
  musicLyrics: boolean;
}

export interface StoredConsent extends ConsentChoices {
  version: number;
  baseUrl: string;
  userEmail: string;
  consentedAt: string;
}

interface StorageArea {
  get(key: string | string[]): Promise<Record<string, unknown>>;
  set(items: Record<string, unknown>): Promise<void>;
  remove(key: string): Promise<void>;
}

export interface ConsentRepository {
  read(): Promise<StoredConsent | null>;
  save(consent: StoredConsent): Promise<StoredConsent>;
  clear(): Promise<void>;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function hasConsentShape(value: Record<string, unknown>): value is Record<string, unknown> & {
  version: number;
  baseUrl: string;
  userEmail: string;
  consentedAt: string;
  watchTracking: boolean;
  catalogCollection: boolean;
  musicLyrics?: boolean;
} {
  return typeof value.version === 'number'
    && Number.isInteger(value.version)
    && typeof value.baseUrl === 'string'
    && typeof value.userEmail === 'string'
    && typeof value.consentedAt === 'string'
    && typeof value.watchTracking === 'boolean'
    && typeof value.catalogCollection === 'boolean'
    && (value.musicLyrics === undefined || typeof value.musicLyrics === 'boolean');
}

function parseConsent(value: unknown): StoredConsent | null {
  if (!isRecord(value) || !hasConsentShape(value)) return null;
  const baseUrl = normalizeBaseUrl(value.baseUrl);
  const userEmail = value.userEmail.trim().toLowerCase();
  if (!baseUrl || !value.consentedAt) return null;
  const musicLyrics = value.musicLyrics === true;
  if (!userEmail && (value.watchTracking || value.catalogCollection || musicLyrics)) return null;
  return {
    version: value.version,
    baseUrl,
    userEmail,
    consentedAt: value.consentedAt,
    watchTracking: value.watchTracking,
    catalogCollection: value.catalogCollection,
    // Older consent records predate the lyrics purpose. Preserve their media
    // choices while keeping the newly introduced purpose denied by default.
    musicLyrics,
  };
}

export function createConsentRepository(storage: StorageArea): ConsentRepository {
  return {
    async read() {
      const stored = await storage.get([CONSENT_STORAGE_KEY, LEGACY_CONSENT_STORAGE_KEY]);
      if (Object.prototype.hasOwnProperty.call(stored, CONSENT_STORAGE_KEY)) {
        return parseConsent(stored[CONSENT_STORAGE_KEY]);
      }
      return parseConsent(stored[LEGACY_CONSENT_STORAGE_KEY]);
    },

    async save(consent) {
      const normalized: StoredConsent = {
        ...consent,
        baseUrl: normalizeBaseUrl(consent.baseUrl),
        userEmail: consent.userEmail.trim().toLowerCase(),
      };
      await storage.set({ [CONSENT_STORAGE_KEY]: normalized });
      await storage.remove(LEGACY_CONSENT_STORAGE_KEY);
      return normalized;
    },

    async clear() {
      await storage.remove(LEGACY_CONSENT_STORAGE_KEY);
      await storage.remove(CONSENT_STORAGE_KEY);
    },
  };
}

export const browserConsentRepository = createConsentRepository({
  get: key => browser.storage.local.get(key),
  set: items => browser.storage.local.set(items),
  remove: key => browser.storage.local.remove(key),
});
