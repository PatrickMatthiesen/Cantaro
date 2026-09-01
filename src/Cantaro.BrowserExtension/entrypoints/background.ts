import { runtimeRouter } from '../platform/background/runtimeRouter';
import { restrictLocalStorageToTrustedContexts } from '../platform/storage/storageAccess';

export default defineBackground(() => {
  void initializeBackground();
});

async function initializeBackground(): Promise<void> {
  await restrictLocalStorageToTrustedContexts();
  await browser.storage.local.remove('cantaro.deliveryQueue.v1');
  browser.runtime.onMessage.addListener(runtimeRouter);
}
