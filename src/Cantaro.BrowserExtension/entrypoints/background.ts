import { broadcastConsentChanged, consentDenyStatus, runtimeRouter } from '../platform/background/runtimeRouter';
import { browserConsentService } from '../platform/consent/consentService';
import { watchSessionIdentityChanges } from '../platform/auth/sessionRepository';
import { restrictLocalStorageToTrustedContexts } from '../platform/storage/storageAccess';
import { initializeContentPreferences } from '../platform/settings/contentPreferences';
import { browserSettingsRepository } from '../platform/settings/settingsRepository';

export default defineBackground(() => {
  void initializeBackground();
});

async function initializeBackground(): Promise<void> {
  await restrictLocalStorageToTrustedContexts();
  await browser.storage.local.remove('cantaro.deliveryQueue.v1');
  initializeContentPreferences();
  watchSessionIdentityChanges(() => {
    const revocation = browserConsentService.revoke();
    broadcastConsentChanged(consentDenyStatus(false));
    void revocation.catch(() => undefined).then(async () => {
      const settings = await browserSettingsRepository.read();
      broadcastConsentChanged(await browserConsentService.getStatus(settings.baseUrl));
    }).catch(() => undefined);
  });
  browser.runtime.onMessage.addListener(runtimeRouter);
}
