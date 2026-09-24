import type { ConsentChoices } from '../consent/consentRepository';
import type { ConsentStatus } from '../consent/consentService';

export type ConsentBackgroundRequest =
  | { type: 'consent.status'; correlationId: string; payload: { baseUrl?: string } }
  | { type: 'consent.save'; correlationId: string; payload: { baseUrl: string } & ConsentChoices }
  | { type: 'consent.revoke'; correlationId: string; payload: { baseUrl: string } };

export type ConsentBackgroundResponse = ConsentStatus;

export type ConsentChangedMessage = {
  type: 'consent.changed';
  payload: ConsentStatus;
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasOptionalBaseUrl(payload: unknown): boolean {
  return isRecord(payload) && (payload.baseUrl === undefined || typeof payload.baseUrl === 'string');
}

function hasBaseUrl(payload: unknown): boolean {
  return isRecord(payload) && typeof payload.baseUrl === 'string';
}

function hasConsentChoices(payload: unknown): boolean {
  return isRecord(payload)
    && typeof payload.baseUrl === 'string'
    && typeof payload.watchTracking === 'boolean'
    && typeof payload.catalogCollection === 'boolean'
    && typeof payload.musicLyrics === 'boolean';
}

export function isConsentBackgroundRequest(value: unknown): value is ConsentBackgroundRequest {
  if (!isRecord(value) || typeof value.correlationId !== 'string') return false;
  if (value.type === 'consent.status') return hasOptionalBaseUrl(value.payload);
  if (value.type === 'consent.revoke') return hasBaseUrl(value.payload);
  return value.type === 'consent.save' && hasConsentChoices(value.payload);
}

export function isConsentChangedMessage(value: unknown): value is ConsentChangedMessage {
  if (!isRecord(value) || value.type !== 'consent.changed' || !isRecord(value.payload)) return false;
  const status = value.payload;
  return Number.isInteger(status.consentVersion)
    && typeof status.needsReview === 'boolean'
    && typeof status.authenticated === 'boolean'
    && typeof status.watchTrackingAllowed === 'boolean'
    && typeof status.catalogCollectionAllowed === 'boolean'
    && typeof status.musicLyricsAllowed === 'boolean';
}
