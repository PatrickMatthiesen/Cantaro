import { normalizeBaseUrl } from '../settings/extensionSettings';
import type { ExtensionUser } from '../auth/extensionSession';
import { browserAuthService } from '../auth/authService';
import {
  browserConsentRepository,
  CURRENT_CONSENT_VERSION,
  type ConsentChoices,
  type ConsentRepository,
} from './consentRepository';

export interface ConsentStatus {
  consentVersion: number;
  needsReview: boolean;
  authenticated: boolean;
  watchTrackingAllowed: boolean;
  catalogCollectionAllowed: boolean;
  musicLyricsAllowed: boolean;
}

export interface ConsentAuthProvider {
  getVerifiedUser(baseUrl: string): Promise<ExtensionUser | null>;
}

export interface ConsentService {
  getStatus(baseUrl: string): Promise<ConsentStatus>;
  save(baseUrl: string, choices: ConsentChoices): Promise<ConsentStatus>;
  revoke(): Promise<void>;
}

function emptyStatus(authenticated = false, needsReview = true): ConsentStatus {
  return {
    consentVersion: CURRENT_CONSENT_VERSION,
    needsReview,
    authenticated,
    watchTrackingAllowed: false,
    catalogCollectionAllowed: false,
    musicLyricsAllowed: false,
  };
}

function sameIdentity(left: string, right: string): boolean {
  return left.trim().toLowerCase() === right.trim().toLowerCase();
}

// Versions 1 and 2 predate the lyrics purpose; the repository maps that choice
// to false. Version 4 added editable local search hints, so versions 3 and 4
// retain their existing explicit lyrics choice.
function isCompatibleConsentVersion(version: number): boolean {
  return version === 1 || version === 2 || version === 3 || version === 4;
}

function isCurrentConsentForApi(
  stored: Awaited<ReturnType<ConsentRepository['read']>>,
  baseUrl: string,
): stored is NonNullable<typeof stored> {
  return Boolean(stored && isCompatibleConsentVersion(stored.version) && stored.baseUrl === baseUrl);
}

function statusForConsent(
  authenticated: boolean,
  allowed: ConsentChoices,
): ConsentStatus {
  return {
    consentVersion: CURRENT_CONSENT_VERSION,
    needsReview: false,
    authenticated,
    watchTrackingAllowed: allowed.watchTracking,
    catalogCollectionAllowed: allowed.catalogCollection,
    musicLyricsAllowed: allowed.musicLyrics,
  };
}

function collectionEnabled(choices: ConsentChoices): boolean {
  return choices.watchTracking || choices.catalogCollection || choices.musicLyrics;
}

function canReuseIdentity(
  existing: Awaited<ReturnType<ConsentRepository['read']>>,
  baseUrl: string,
  choices: ConsentChoices,
): boolean {
  if (!existing?.userEmail) return false;
  return isCompatibleConsentVersion(existing.version)
    && existing.baseUrl === baseUrl
    && (!choices.watchTracking || existing.watchTracking)
    && (!choices.catalogCollection || existing.catalogCollection)
    && (!choices.musicLyrics || existing.musicLyrics);
}

async function resolveSaveIdentity(
  repository: ConsentRepository,
  authProvider: ConsentAuthProvider,
  baseUrl: string,
  choices: ConsentChoices,
): Promise<{
  existing: Awaited<ReturnType<ConsentRepository['read']>>;
  user: ExtensionUser | null;
}> {
  const existing = await repository.read();
  if (!collectionEnabled(choices) || canReuseIdentity(existing, baseUrl, choices)) {
    return { existing, user: null };
  }
  const user = await authProvider.getVerifiedUser(baseUrl);
  if (!user) throw new Error('Sign in to Cantaro before enabling extension collection.');
  return { existing, user };
}

async function verifyConsentUser(
  authProvider: ConsentAuthProvider,
  baseUrl: string,
): Promise<ExtensionUser | null> {
  try {
    return await authProvider.getVerifiedUser(baseUrl);
  } catch {
    return null;
  }
}

function statusFromStoredConsent(
  stored: Awaited<ReturnType<ConsentRepository['read']>>,
  baseUrl: string,
  user: ExtensionUser | null,
): ConsentStatus {
  const current = isCurrentConsentForApi(stored, baseUrl);
  const identityMatches = current && (!stored.userEmail || sameIdentity(stored.userEmail, user?.email ?? ''));
  const deniedForApi = current && !stored.userEmail && !stored.watchTracking
    && !stored.catalogCollection && !stored.musicLyrics;
  if (!user) return emptyStatus(false, !deniedForApi);
  if (!identityMatches) return emptyStatus(true);
  return statusForConsent(true, stored);
}

export function createConsentService(
  repository: ConsentRepository,
  authProvider: ConsentAuthProvider,
): ConsentService {
  let revision = 0;
  let inMemoryRevoked = false;
  let writeQueue: Promise<unknown> = Promise.resolve();

  function enqueueWrite<T>(operation: () => Promise<T>): Promise<T> {
    const next = writeQueue.then(operation);
    writeQueue = next.catch(() => undefined);
    return next;
  }

  return {
    async getStatus(baseUrl) {
      const readRevision = revision;
      await writeQueue;
      if (readRevision !== revision) return emptyStatus(false);
      const normalizedBaseUrl = normalizeBaseUrl(baseUrl);
      const user = await verifyConsentUser(authProvider, normalizedBaseUrl);
      if (readRevision !== revision) return emptyStatus(false);
      await writeQueue;
      if (readRevision !== revision) return emptyStatus(false);
      if (inMemoryRevoked) return emptyStatus(Boolean(user));
      const stored = await repository.read();
      if (readRevision !== revision) return emptyStatus(false);
      return statusFromStoredConsent(stored, normalizedBaseUrl, user);
    },

    async save(baseUrl, choices) {
      const saveRevision = ++revision;
      inMemoryRevoked = true;
      const normalizedBaseUrl = normalizeBaseUrl(baseUrl);
      const { existing, user } = await resolveSaveIdentity(
        repository, authProvider, normalizedBaseUrl, choices,
      );
      if (saveRevision !== revision) throw new Error('Collection consent changed while it was being saved.');
      const userEmail = collectionEnabled(choices)
        ? user?.email ?? existing?.userEmail ?? ''
        : '';
      await enqueueWrite(() => repository.save({
        version: CURRENT_CONSENT_VERSION,
        baseUrl: normalizedBaseUrl,
        userEmail,
        consentedAt: new Date().toISOString(),
        watchTracking: choices.watchTracking === true,
        catalogCollection: choices.catalogCollection === true,
        musicLyrics: choices.musicLyrics === true,
      }));
      if (saveRevision !== revision) throw new Error('Collection consent changed while it was being saved.');
      inMemoryRevoked = false;
      return statusForConsent(Boolean(user), {
        watchTracking: user ? choices.watchTracking === true : false,
        catalogCollection: user ? choices.catalogCollection === true : false,
        musicLyrics: user ? choices.musicLyrics === true : false,
      });
    },

    revoke() {
      revision++;
      inMemoryRevoked = true;
      return enqueueWrite(() => repository.clear());
    },
  };
}

export const browserConsentService = createConsentService(
  browserConsentRepository,
  browserAuthService,
);
