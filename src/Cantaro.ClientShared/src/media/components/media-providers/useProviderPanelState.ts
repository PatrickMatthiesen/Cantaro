import { useCallback, useEffect, useRef, useState } from 'react';
import type { Dispatch, MutableRefObject, SetStateAction } from 'react';
import { mediaApi } from '../../services/mediaApi';
import { clearStoredValue, remoteCheckTimestampKey, writeStoredValue } from '../../services/mediaRefreshCache';
import type {
  MediaImportDto,
  MediaImportRequestDto,
  MediaInitialSyncApplyResultDto,
  MediaInitialSyncPreviewDto,
  MediaLibraryImportEventDto,
  MediaProviderAccountStatusDto,
} from '../../services/mediaApi';

function shouldAutoPreviewAfterConnect(providerId: string): boolean {
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

function useProviderInitialSync(
  providerId: string,
  isConnected: boolean,
  handleImport: () => Promise<MediaImportRequestDto | null>,
  setError: (error: string | null) => void,
  hasTriggeredConnectedPreview: MutableRefObject<boolean>,
) {
  const [initialSyncPreview, setInitialSyncPreview] = useState<MediaInitialSyncPreviewDto | null>(null);
  const [initialSyncResult, setInitialSyncResult] = useState<MediaInitialSyncApplyResultDto | null>(null);
  const [isPreviewingInitialSync, setIsPreviewingInitialSync] = useState(false);
  const [isApplyingInitialSync, setIsApplyingInitialSync] = useState(false);
  const previewRequestVersion = useRef(0);
  const previewAbortController = useRef<AbortController | null>(null);

  const handlePreviewInitialSync = useCallback(async () => {
    const requestVersion = ++previewRequestVersion.current;
    previewAbortController.current?.abort();
    const abortController = new AbortController();
    previewAbortController.current = abortController;
    setIsPreviewingInitialSync(true);
    setInitialSyncResult(null);
    setError(null);

    try {
      const preview = await mediaApi.previewInitialSync(providerId, abortController.signal);
      if (requestVersion !== previewRequestVersion.current) {
        return null;
      }
      setInitialSyncPreview(preview);
      return preview;
    } catch (error) {
      if ((error as { name?: string }).name === 'AbortError') {
        return null;
      }
      if (requestVersion === previewRequestVersion.current) {
        setError('Cantaro couldn’t prepare this sync. Your provider libraries have not been changed.');
      }
      return null;
    } finally {
      if (requestVersion === previewRequestVersion.current) {
        setIsPreviewingInitialSync(false);
        previewAbortController.current = null;
      }
    }
  }, [providerId, setError]);

  useEffect(() => {
    if (initialSyncPreview?.status !== 'settling') {
      return;
    }

    const timeoutId = window.setTimeout(() => {
      void handlePreviewInitialSync();
    }, 1500);
    return () => window.clearTimeout(timeoutId);
  }, [handlePreviewInitialSync, initialSyncPreview]);

  useEffect(() => {
    if (!isConnected || hasTriggeredConnectedPreview.current || !shouldAutoPreviewAfterConnect(providerId)) {
      return;
    }

    hasTriggeredConnectedPreview.current = true;
    void handlePreviewInitialSync().then(async (preview) => {
      if (preview?.status !== 'import-required') {
        return;
      }

      await handleImport();
      setInitialSyncPreview(null);
      clearConnectSearchParams();
    });
  }, [handleImport, handlePreviewInitialSync, hasTriggeredConnectedPreview, isConnected, providerId]);

  useEffect(() => {
    const batchId = initialSyncResult?.status === 'queued' ? initialSyncResult.batchId : undefined;
    if (!batchId) {
      return;
    }

    let isCancelled = false;
    let timeoutId: number | undefined;
    // fallow-ignore-next-line complexity
    const poll = async () => {
      if (isCancelled) {
        return;
      }
      try {
        const progress = await mediaApi.getInitialSyncProgress(providerId, batchId);
        if (isCancelled) {
          return;
        }
        if (progress.status === 'completed') {
          setInitialSyncResult((current) => current ? { ...current, status: 'completed' } : current);
          return;
        }
        if (progress.status === 'failed') {
          setInitialSyncResult((current) => current ? {
            ...current,
            status: 'failed',
            failedOperations: progress.failedOperations,
          } : current);
          return;
        }
        timeoutId = window.setTimeout(() => void poll(), 2000);
      } catch {
        if (!isCancelled) {
          timeoutId = window.setTimeout(() => void poll(), 5000);
        }
      }
    };

    void poll();
    return () => {
      isCancelled = true;
      if (timeoutId !== undefined) {
        window.clearTimeout(timeoutId);
      }
    };
  }, [initialSyncResult?.batchId, initialSyncResult?.status, providerId]);

  useEffect(() => {
    if (isConnected) {
      return;
    }

    previewRequestVersion.current++;
    previewAbortController.current?.abort();
    previewAbortController.current = null;
    setInitialSyncPreview(null);
    setInitialSyncResult(null);
    setIsPreviewingInitialSync(false);
    setIsApplyingInitialSync(false);
  }, [isConnected]);

  const handleApplyInitialSync = useCallback(async () => {
    if (!initialSyncPreview || initialSyncPreview.status !== 'ready' || !initialSyncPreview.fingerprint) {
      setError('This comparison is no longer current. Prepare a new preview before syncing.');
      return null;
    }

    setIsApplyingInitialSync(true);
    setError(null);
    try {
      const result = await mediaApi.applyInitialSync(providerId, {
        fingerprint: initialSyncPreview.fingerprint,
      });
      setInitialSyncResult(result);
      clearConnectSearchParams();
      return result;
    } catch (error) {
      if ((error as { status?: number }).status === 409) {
        void handlePreviewInitialSync();
      } else {
        setError('Cantaro couldn’t start this sync. Nothing new was sent to the provider.');
      }
      return null;
    } finally {
      setIsApplyingInitialSync(false);
    }
  }, [handlePreviewInitialSync, initialSyncPreview, providerId, setError]);

  const handleDismissInitialSync = useCallback(() => {
    previewRequestVersion.current++;
    previewAbortController.current?.abort();
    previewAbortController.current = null;
    setInitialSyncPreview(null);
    setInitialSyncResult(null);
    setIsPreviewingInitialSync(false);
    clearConnectSearchParams();
  }, []);

  return {
    initialSyncPreview,
    initialSyncResult,
    isPreviewingInitialSync,
    isApplyingInitialSync,
    handlePreviewInitialSync,
    handleApplyInitialSync,
    handleDismissInitialSync,
  };
}

function useProviderDisconnect(
  providerId: string,
  setError: (error: string | null) => void,
  setStatus: Dispatch<SetStateAction<MediaProviderAccountStatusDto | null>>,
  setLastImport: (value: MediaImportDto | null) => void,
  hasTriggeredConnectedPreview: MutableRefObject<boolean>,
) {
  const [isDisconnecting, setIsDisconnecting] = useState(false);

  const handleDisconnect = useCallback(async () => {
    setIsDisconnecting(true);
    setError(null);
    hasTriggeredConnectedPreview.current = false;

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
  }, [hasTriggeredConnectedPreview, providerId, setError, setLastImport, setStatus]);

  return {
    isDisconnecting,
    handleDisconnect,
  };
}

export function useProviderPanelState(providerId: string) {
  const hasTriggeredConnectedPreview = useRef(false);
  const { status, setStatus, isLoadingStatus, error, setError, reloadStatus } = useProviderStatus(providerId);
  const { isImporting, lastImport, setLastImport, handleImport } = useProviderImport(providerId, setError);
  const initialSync = useProviderInitialSync(
    providerId,
    Boolean(status?.isConnected),
    handleImport,
    setError,
    hasTriggeredConnectedPreview,
  );
  const { isDisconnecting, handleDisconnect } = useProviderDisconnect(
    providerId,
    setError,
    setStatus,
    setLastImport,
    hasTriggeredConnectedPreview,
  );

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
    ...initialSync,
  };
}
