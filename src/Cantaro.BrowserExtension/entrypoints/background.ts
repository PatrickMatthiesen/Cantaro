import { broadcastConsentChanged, consentDenyStatus, runtimeRouter } from '../platform/background/runtimeRouter';
import { browserConsentService } from '../platform/consent/consentService';
import { watchSessionIdentityChanges } from '../platform/auth/sessionRepository';
import { messageFailure } from '../platform/messaging/messageResult';
import { restrictLocalStorageToTrustedContexts } from '../platform/storage/storageAccess';
import { initializeContentPreferences } from '../platform/settings/contentPreferences';
import { browserSettingsRepository } from '../platform/settings/settingsRepository';

export default defineBackground(() => {
  const storageReady = restrictLocalStorageToTrustedContexts();
  // Chrome can dispatch the message that woke this worker during initialization.
  browser.runtime.onMessage.addListener((message: unknown, sender, sendResponse) => {
    if (!message || typeof message !== 'object' || !('correlationId' in message)
      || typeof message.correlationId !== 'string') return;
    const { correlationId } = message;
    void storageReady.then(() => runtimeRouter(message, sender)).then(
      response => sendResponse(response),
      () => sendResponse(messageFailure(correlationId, 'unexpected', 'The extension background request failed.', true)),
    );
    return true;
  });
  void storageReady.then(() => browser.storage.local.remove('cantaro.deliveryQueue.v1')).catch(() => undefined);
  initializeContentPreferences();
  watchSessionIdentityChanges(() => {
    const revocation = browserConsentService.revoke();
    broadcastConsentChanged(consentDenyStatus(false));
    void revocation.catch(() => undefined).then(async () => {
      const settings = await browserSettingsRepository.read();
      broadcastConsentChanged(await browserConsentService.getStatus(settings.baseUrl));
    }).catch(() => undefined);
  });
});
