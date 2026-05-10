import { useCallback, useEffect, useRef, useState } from 'react';
import type { Dispatch, MutableRefObject, SetStateAction } from 'react';
import { mediaApi } from '../../services/mediaApi';
import { clearStoredValue, remoteCheckTimestampKey, writeStoredValue } from '../../services/mediaRefreshCache';
import type { MediaImportDto, MediaProviderAccountStatusDto } from '../../services/mediaApi';

function shouldAutoImportAfterConnect(providerId: string): boolean {
  const search = new URLSearchParams(window.location.search);
  return search.get('connected') === 'true' && search.get('provider') === providerId;
}

function clearConnectSearchParams(): void {
  const url = new URL(window.location.href);
  url.searchParams.delete('connected');
  url.searchParams.delete('provider');
  url.searchParams.delete('trigger');
  window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`);
}

function getErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

function useProviderStatus(providerId: string) {
  const [status, setStatus] = useState<MediaProviderAccountStatusDto | null>(null);
  const [isLoadingStatus, setIsLoadingStatus] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadStatus = useCallback(async () => {
    setIsLoadingStatus(true);
    setError(null);

    try {
      setStatus(await mediaApi.getProviderStatus(providerId));
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load status'));
    } finally {
      setIsLoadingStatus(false);
    }
  }, [providerId]);

  useEffect(() => {
    void loadStatus();
  }, [loadStatus]);

  return {
    status,
    setStatus,
    isLoadingStatus,
    error,
    setError,
  };
}

function useProviderImport(providerId: string, setError: (error: string | null) => void) {
  const [isImporting, setIsImporting] = useState(false);
  const [lastImport, setLastImport] = useState<MediaImportDto | null>(null);

  const handleImport = useCallback(async () => {
    setIsImporting(true);
    setError(null);

    try {
      const result = await mediaApi.importLibrary(providerId);
      setLastImport(result);
      writeStoredValue(remoteCheckTimestampKey(providerId), result.importedAt);
      return result;
    } catch (importError) {
      setError(getErrorMessage(importError, 'Import failed'));
      return null;
    } finally {
      setIsImporting(false);
    }
  }, [providerId, setError]);

  return {
    isImporting,
    lastImport,
    setLastImport,
    handleImport,
  };
}

function useProviderDisconnect(
  providerId: string,
  setError: (error: string | null) => void,
  setStatus: Dispatch<SetStateAction<MediaProviderAccountStatusDto | null>>,
  setLastImport: (value: MediaImportDto | null) => void,
  hasTriggeredConnectedImport: MutableRefObject<boolean>,
) {
  const [isDisconnecting, setIsDisconnecting] = useState(false);

  const handleDisconnect = useCallback(async () => {
    setIsDisconnecting(true);
    setError(null);
    hasTriggeredConnectedImport.current = false;

    try {
      await mediaApi.disconnectProvider(providerId);
      setStatus((previous) => (previous ? { ...previous, isConnected: false, displayName: undefined } : null));
      setLastImport(null);
      clearStoredValue(remoteCheckTimestampKey(providerId));
    } catch (disconnectError) {
      setError(getErrorMessage(disconnectError, 'Failed to disconnect'));
    } finally {
      setIsDisconnecting(false);
    }
  }, [hasTriggeredConnectedImport, providerId, setError, setLastImport, setStatus]);

  return {
    isDisconnecting,
    handleDisconnect,
  };
}

function useAutoImportAfterConnect(
  providerId: string,
  isConnected: boolean,
  handleImport: () => Promise<MediaImportDto | null>,
  hasTriggeredConnectedImport: MutableRefObject<boolean>,
) {
  useEffect(() => {
    if (!isConnected || hasTriggeredConnectedImport.current || !shouldAutoImportAfterConnect(providerId)) {
      return;
    }

    hasTriggeredConnectedImport.current = true;
    void handleImport().finally(() => {
      clearConnectSearchParams();
    });
  }, [handleImport, hasTriggeredConnectedImport, isConnected, providerId]);
}

export function useProviderPanelState(providerId: string) {
  const hasTriggeredConnectedImport = useRef(false);
  const { status, setStatus, isLoadingStatus, error, setError } = useProviderStatus(providerId);
  const { isImporting, lastImport, setLastImport, handleImport } = useProviderImport(providerId, setError);
  const { isDisconnecting, handleDisconnect } = useProviderDisconnect(
    providerId,
    setError,
    setStatus,
    setLastImport,
    hasTriggeredConnectedImport,
  );

  useAutoImportAfterConnect(providerId, Boolean(status?.isConnected), handleImport, hasTriggeredConnectedImport);

  const handleConnect = useCallback(() => {
    mediaApi.connectProvider(providerId, {
      route: window.location.pathname,
      trigger: 'media-providers-page',
    });
  }, [providerId]);

  return {
    status,
    isLoadingStatus,
    error,
    isImporting,
    isDisconnecting,
    lastImport,
    handleConnect,
    handleDisconnect,
    handleImport,
  };
}
