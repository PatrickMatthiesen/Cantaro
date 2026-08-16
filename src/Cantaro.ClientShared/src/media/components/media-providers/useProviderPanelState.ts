import { useCallback, useEffect, useRef, useState } from 'react';
import type { Dispatch, MutableRefObject, SetStateAction } from 'react';
import { mediaApi } from '../../services/mediaApi';
import { clearStoredValue, remoteCheckTimestampKey, writeStoredValue } from '../../services/mediaRefreshCache';
import type { MediaImportDto, MediaImportRequestDto, MediaLibraryImportEventDto, MediaProviderAccountStatusDto } from '../../services/mediaApi';

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

function useProviderStatus(providerId: string) {
  const [status, setStatus] = useState<MediaProviderAccountStatusDto | null>(null);
  const [isLoadingStatus, setIsLoadingStatus] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadStatus = useCallback(async () => {
    setIsLoadingStatus(true);
    setError(null);

    try {
      setStatus(await mediaApi.getProviderStatus(providerId));
    } catch {
      setError('Cantaro couldn’t check this connection. Retry when the provider is available.');
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
    reloadStatus: loadStatus,
  };
}

function useProviderImport(providerId: string, setError: (error: string | null) => void) {
  const [isImporting, setIsImporting] = useState(false);
  const [lastImport, setLastImport] = useState<MediaImportDto | null>(null);

  useEffect(() => {
    let eventSource: EventSource | null = null;
    let isMounted = true;

    void mediaApi.subscribeToImportEvents(
      providerId,
      undefined,
      (event: MediaLibraryImportEventDto) => {
        if (!isMounted) {
          return;
        }

        if (event.status === 'running') {
          setIsImporting(true);
          return;
        }

        if (event.status === 'completed') {
          const result = {
            providerId: event.providerId,
            importedCount: event.importedCount,
            createdTitles: event.createdTitles,
            createdEntries: event.createdEntries,
            updatedEntries: event.updatedEntries,
            importedAt: event.occurredAt,
          };
          setLastImport(result);
          writeStoredValue(remoteCheckTimestampKey(providerId), event.occurredAt);
          setIsImporting(false);
          return;
        }

        if (event.status === 'failed') {
          setError('The provider couldn’t be refreshed. Cantaro will keep using your saved library data.');
          setIsImporting(false);
        }
      },
      () => {
        if (isMounted) {
          setIsImporting(false);
        }
      },
    ).then((source) => {
      eventSource = source;
    });

    return () => {
      isMounted = false;
      eventSource?.close();
    };
  }, [providerId, setError]);

  const handleImport = useCallback(async (): Promise<MediaImportRequestDto | null> => {
    setIsImporting(true);
    setError(null);

    try {
      return await mediaApi.importLibrary(providerId);
    } catch {
      setError('The provider couldn’t be refreshed. Cantaro will keep using your saved library data.');
      setIsImporting(false);
      return null;
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
    } catch {
      setError('Cantaro couldn’t disconnect this provider. Please try again.');
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
  handleImport: () => Promise<MediaImportRequestDto | null>,
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
  const { status, setStatus, isLoadingStatus, error, setError, reloadStatus } = useProviderStatus(providerId);
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
    reloadStatus,
  };
}
