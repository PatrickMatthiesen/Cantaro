import { mediaRequestHandler } from '../features/media/background/mediaRequestHandler';
import { runtimeRouter } from '../platform/background/runtimeRouter';

export default defineBackground(() => {
  browser.runtime.onMessage.addListener(runtimeRouter);
  void mediaRequestHandler.drainQueue();
});
