import { useEffect, useMemo, useRef, useState } from 'react';
import type {
  ActiveTabContextState,
  ExtensionAppServices,
} from './extensionAppTypes';
import { isTabContextChangedNotification } from '../../platform/messaging/tabContext';

export function subscribeToActiveTabContextChanges(
  refresh: () => void,
  refreshContext = refresh,
): () => void {
  const handleActivated = () => refresh();
  const handleUpdated = (_tabId: number, change: { status?: string; url?: string }) => {
    if (change.status === 'complete' || change.url) refresh();
  };
  const handleMessage = (message: unknown) => {
    if (isTabContextChangedNotification(message)) refreshContext();
    return undefined;
  };
  browser.tabs.onActivated.addListener(handleActivated);
  browser.tabs.onUpdated.addListener(handleUpdated);
  browser.runtime.onMessage.addListener(handleMessage);
  return () => {
    browser.tabs.onActivated.removeListener(handleActivated);
    browser.tabs.onUpdated.removeListener(handleUpdated);
    browser.runtime.onMessage.removeListener(handleMessage);
  };
}

export function createActiveTabContextRefresher(
  services: ExtensionAppServices,
  setState: (state: ActiveTabContextState) => void,
): (showLoading?: boolean) => Promise<void> {
  let latestRequestId = 0;
  return async (showLoading = true) => {
    const requestId = ++latestRequestId;
    if (showLoading) setState({ status: 'loading' });
    try {
      const snapshot = await services.readActiveTabContext();
      if (requestId === latestRequestId) setState({ status: 'available', snapshot });
    } catch (error) {
      if (requestId !== latestRequestId) return;
      setState({
        status: 'unavailable',
        reason: error instanceof Error ? error.message : 'Could not inspect the active tab.',
      });
    }
  };
}

export function useActiveTabContext(services: ExtensionAppServices, enabled = true) {
  const [state, setState] = useState<ActiveTabContextState>({ status: 'loading' });
  const currentRefresh = useRef<object>(null);
  const refresh = useMemo(
    () => {
      const identity = {};
      currentRefresh.current = identity;
      return createActiveTabContextRefresher(services, next => {
        if (enabled && currentRefresh.current === identity) setState(next);
      });
    },
    [services, enabled],
  );

  useEffect(() => {
    if (enabled) void refresh();
  }, [refresh, enabled]);

  useEffect(() => {
    if (!enabled) return;
    return subscribeToActiveTabContextChanges(
      () => { void refresh(); },
      () => { void refresh(false); },
    );
  }, [refresh, enabled]);

  return { state: enabled ? state : { status: 'available', snapshot: { feature: 'unsupported', pageKind: 'unsupported', pageUrl: '' } } as ActiveTabContextState, refresh };
}
