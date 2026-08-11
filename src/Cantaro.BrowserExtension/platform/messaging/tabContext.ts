import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import type { MediaTabContext } from '../../features/media/contracts/mediaTabContext';
import type { MusicTabContext } from '../../features/music/contracts/musicTabContext';
import { messageSuccess } from './messageResult';

export type TabContextSnapshot = MediaTabContext | MusicTabContext;

interface GetTabContextRequest {
  type: 'tab.context.get';
  correlationId: string;
}

interface TabContextChangedNotification {
  type: 'tab.context.changed';
}

export function isTabContextChangedNotification(
  value: unknown,
): value is TabContextChangedNotification {
  return Boolean(value && typeof value === 'object'
    && (value as { type?: unknown }).type === 'tab.context.changed');
}

function isGetTabContextRequest(value: unknown): value is GetTabContextRequest {
  return Boolean(value && typeof value === 'object'
    && (value as { type?: unknown }).type === 'tab.context.get'
    && typeof (value as { correlationId?: unknown }).correlationId === 'string');
}

export function registerTabContext(
  ctx: ContentScriptContext,
  getSnapshot: () => TabContextSnapshot,
  isActive: () => boolean = () => true,
): () => void {
  const onMessage = (message: unknown) => {
    if (!isGetTabContextRequest(message)) return undefined;
    if (!isActive()) return undefined;
    return Promise.resolve(messageSuccess(getSnapshot(), message.correlationId));
  };
  browser.runtime.onMessage.addListener(onMessage);
  ctx.onInvalidated(() => browser.runtime.onMessage.removeListener(onMessage));
  return () => {
    void browser.runtime.sendMessage({ type: 'tab.context.changed' })
      .catch(() => undefined);
  };
}
