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

export function isConsentBackgroundRequest(value: unknown): value is ConsentBackgroundRequest {
  if (!value || typeof value !== 'object') return false;
  const request = value as { type?: unknown; correlationId?: unknown; payload?: unknown };
  if (typeof request.correlationId !== 'string' || !request.payload || typeof request.payload !== 'object') return false;
  const payload = request.payload as { baseUrl?: unknown; watchTracking?: unknown; catalogCollection?: unknown };
  if (request.type === 'consent.status') return payload.baseUrl === undefined || typeof payload.baseUrl === 'string';
  if (typeof payload.baseUrl !== 'string') return false;
  if (request.type === 'consent.revoke') return true;
  return request.type === 'consent.save'
    && typeof payload.watchTracking === 'boolean'
    && typeof payload.catalogCollection === 'boolean';
}

export function isConsentChangedMessage(value: unknown): value is ConsentChangedMessage {
  if (!value || typeof value !== 'object') return false;
  const message = value as { type?: unknown; payload?: unknown };
  if (message.type !== 'consent.changed' || !message.payload || typeof message.payload !== 'object') return false;
  const status = message.payload as Record<string, unknown>;
  return Number.isInteger(status.consentVersion)
    && typeof status.needsReview === 'boolean'
    && typeof status.authenticated === 'boolean'
    && typeof status.watchTrackingAllowed === 'boolean'
    && typeof status.catalogCollectionAllowed === 'boolean';
}
