import type {
  ConsentBackgroundRequest,
  ConsentBackgroundResponse,
} from '../messaging/consentMessages';
import { isConsentChangedMessage, type ConsentChangedMessage } from '../messaging/consentMessages';
import { createCorrelationId, type MessageResult } from '../messaging/messageResult';
import type { ConsentChoices } from './consentRepository';
import type { ConsentStatus } from './consentService';

interface ContentConsentStatus {
  authenticated: boolean;
  watch: boolean;
  catalog: boolean;
}

function toContentStatus(status: ConsentStatus): ContentConsentStatus {
  return {
    authenticated: status.authenticated,
    watch: status.watchTrackingAllowed,
    catalog: status.catalogCollectionAllowed,
  };
}

function denyStatus(version: number): ConsentStatus {
  return {
    consentVersion: version,
    needsReview: true,
    authenticated: false,
    watchTrackingAllowed: false,
    catalogCollectionAllowed: false,
  };
}

async function sendConsentRequest<T extends ConsentBackgroundResponse>(
  request: ConsentBackgroundRequest,
): Promise<T> {
  const result = await browser.runtime.sendMessage(request) as MessageResult<T>;
  if (!result?.ok) throw new Error(result?.error.message ?? 'The extension background service did not respond.');
  return result.value;
}

export function getRuntimeConsentStatus(baseUrl?: string): Promise<ConsentStatus> {
  return sendConsentRequest({
    type: 'consent.status', correlationId: createCorrelationId(), payload: baseUrl ? { baseUrl } : {},
  });
}

export function saveRuntimeConsent(baseUrl: string, choices: ConsentChoices): Promise<ConsentStatus> {
  return sendConsentRequest({
    type: 'consent.save', correlationId: createCorrelationId(), payload: { baseUrl, ...choices },
  });
}

export async function revokeRuntimeConsent(baseUrl: string): Promise<void> {
  await sendConsentRequest({
    type: 'consent.revoke', correlationId: createCorrelationId(), payload: { baseUrl },
  });
}

export function subscribeRuntimeConsent(
  listener: (status: ConsentStatus) => void,
): () => void {
  let active = true;
  let notificationRevision = 0;
  const onMessage = (message: unknown) => {
    if (isConsentChangedMessage(message)) {
      const revision = ++notificationRevision;
      const status = (message as ConsentChangedMessage).payload;
      const permitsCollection = status.authenticated
        && (status.watchTrackingAllowed || status.catalogCollectionAllowed);
      if (!active) return;
      if (!permitsCollection) {
        listener(status);
        return;
      }
      // Tab delivery is asynchronous and an older allow notification may
      // arrive after a revoke. Apply a local deny first, then confirm the
      // current background state before re-enabling collection.
      listener(denyStatus(status.consentVersion));
      void getRuntimeConsentStatus().then(next => {
        if (active && revision === notificationRevision) listener(next);
      }).catch(() => undefined);
    }
  };
  browser.runtime.onMessage.addListener(onMessage);
  return () => {
    active = false;
    browser.runtime.onMessage.removeListener(onMessage);
  };
}

/** Redacted purpose-specific contract for website content scripts. */
export function readContentConsentStatus(baseUrl?: string): Promise<ContentConsentStatus> {
  return getRuntimeConsentStatus(baseUrl).then(toContentStatus);
}

export function watchContentConsentStatus(
  listener: (status: ContentConsentStatus) => void,
): () => void {
  return subscribeRuntimeConsent(status => listener(toContentStatus(status)));
}
