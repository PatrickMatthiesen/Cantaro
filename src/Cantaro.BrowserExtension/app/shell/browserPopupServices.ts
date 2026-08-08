import type {
  ExtensionAppServices,
  TabContextMessageResult,
  PopupTabContextSnapshot,
} from './extensionAppTypes';
import { createCorrelationId } from '../../platform/messaging/messageResult';

function unsupportedSnapshot(pageUrl = ''): PopupTabContextSnapshot {
  return {
    feature: 'unsupported',
    pageKind: 'unsupported',
    pageUrl,
  };
}

async function readActiveTabContext(): Promise<PopupTabContextSnapshot> {
  const [activeTab] = await browser.tabs.query({ active: true, currentWindow: true });
  if (!activeTab?.id) return unsupportedSnapshot(activeTab?.url);

  let result: TabContextMessageResult;
  try {
    result = await browser.tabs.sendMessage(activeTab.id, {
      type: 'tab.context.get',
      correlationId: createCorrelationId(),
    }) as TabContextMessageResult;
  } catch {
    return unsupportedSnapshot(activeTab.url);
  }

  if (result.ok) return result.value;
  throw new Error(result.error.message);
}

export const browserPopupServices: ExtensionAppServices = {
  readActiveTabContext,
};
