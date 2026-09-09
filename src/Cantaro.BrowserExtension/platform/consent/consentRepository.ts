import { normalizeBaseUrl } from '../settings/extensionSettings';

export const CONSENT_STORAGE_KEY = 'cantaro.consent.v1';
export const CURRENT_CONSENT_VERSION = 1;

export interface ConsentChoices {
  watchTracking: boolean;
  catalogCollection: boolean;
}

export interface StoredConsent extends ConsentChoices {
  version: number;
  baseUrl: string;
  userEmail: string;
  consentedAt: string;
}

interface StorageArea {
  get(key: string): Promise<Record<string, unknown>>;
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
} {
  return typeof value.version === 'number'
    && Number.isInteger(value.version)
    && typeof value.baseUrl === 'string'
    && typeof value.userEmail === 'string'
    && typeof value.consentedAt === 'string'
    && typeof value.watchTracking === 'boolean'
    && typeof value.catalogCollection === 'boolean';
}

function parseConsent(value: unknown): StoredConsent | null {
  if (!isRecord(value) || !hasConsentShape(value)) return null;
  const baseUrl = normalizeBaseUrl(value.baseUrl);
  const userEmail = value.userEmail.trim().toLowerCase();
  if (!baseUrl || !value.consentedAt) return null;
  if (!userEmail && (value.watchTracking || value.catalogCollection)) return null;
  return {
    version: value.version,
    baseUrl,
    userEmail,
    consentedAt: value.consentedAt,
    watchTracking: value.watchTracking,
    catalogCollection: value.catalogCollection,
  };
}

export function createConsentRepository(storage: StorageArea): ConsentRepository {
  return {
    async read() {
      const stored = await storage.get(CONSENT_STORAGE_KEY);
      return parseConsent(stored[CONSENT_STORAGE_KEY]);
    },

    async save(consent) {
      const normalized: StoredConsent = {
        ...consent,
        baseUrl: normalizeBaseUrl(consent.baseUrl),
        userEmail: consent.userEmail.trim().toLowerCase(),
      };
      await storage.set({ [CONSENT_STORAGE_KEY]: normalized });
      return normalized;
    },

    clear() {
      return storage.remove(CONSENT_STORAGE_KEY);
    },
  };
}

export const browserConsentRepository = createConsentRepository({
  get: key => browser.storage.local.get(key),
  set: items => browser.storage.local.set(items),
  remove: key => browser.storage.local.remove(key),
});
