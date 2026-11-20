import { useCallback, useEffect, useRef, useState } from 'react';
import type { ShowSnackbar } from '../../../ui';
import { mediaApi, type MediaEntryDetailModel } from '../../services/mediaApi';
import { mainMediaProviderId } from '../../services/mediaProviders';
import {
  isRemoteCheckStale,
  readStoredValue,
  remoteCheckTimestampKey,
  writeStoredValue,
} from '../../services/mediaRefreshCache';

function getImportErrorMessage(error: unknown, fallbackMessage: string): string {
  const errorCode = (error as { code?: string } | null)?.code;
  return errorCode === 'platform_reconnect_required'
    ? 'Reconnect this provider in Settings to refresh its library.' : fallbackMessage;
}

async function refreshEntryFromProvider(
  providerId: string,
  signal: AbortSignal,
  reloadEntry: () => Promise<MediaEntryDetailModel>,
  showSnackbar: ShowSnackbar,
  errorMessage: string,
  successMessage?: string,
) {
  try {
    const event = await mediaApi.importLibraryAndWait(providerId, signal);
    if (signal.aborted) return;
    writeStoredValue(remoteCheckTimestampKey(providerId), event.occurredAt);
    await reloadEntry();

    if (successMessage && !signal.aborted) {
      showSnackbar({ message: successMessage, variant: 'success' });
    }
  } catch (error) {
    if (!signal.aborted) showSnackbar({
      message: getImportErrorMessage(error, errorMessage),
      variant: 'error',
    });
  }
}

export function useRemoteEntryRefresh(
  entry: MediaEntryDetailModel | null,
  reloadEntry: () => Promise<MediaEntryDetailModel>,
  showSnackbar: ShowSnackbar,
) {
  const [isRefreshingRemote, setIsRefreshingRemote] = useState(false);
  const entryId = entry?.id;
  const entryProvider = entry?.provider;
  const entryIsConnected = entry?.isConnected;

  useEffect(() => {
    const refreshProviderId = entryProvider || mainMediaProviderId;
    if (!entryId || !entryIsConnected
      || !isRemoteCheckStale(readStoredValue(remoteCheckTimestampKey(refreshProviderId)))) {
      setIsRefreshingRemote(false);
      return;
    }

    const abortController = new AbortController();

    const refreshFromRemote = async () => {
      setIsRefreshingRemote(true);

      try {
        await refreshEntryFromProvider(
          refreshProviderId,
          abortController.signal,
          reloadEntry,
          showSnackbar,
          'Could not refresh this entry. Try again.',
        );
      } finally {
        if (!abortController.signal.aborted) setIsRefreshingRemote(false);
      }
    };

    void refreshFromRemote();
    return () => abortController.abort();
  }, [entryId, entryIsConnected, entryProvider, reloadEntry, showSnackbar]);

  return isRefreshingRemote;
}

export function useManualRemoteRefresh(
  entry: MediaEntryDetailModel | null,
  reloadEntry: () => Promise<MediaEntryDetailModel>,
  showSnackbar: ShowSnackbar,
) {
  const [isRefreshingRemote, setIsRefreshingRemote] = useState(false);
  const abortControllerRef = useRef<AbortController | null>(null);
  const entryId = entry?.id;

  useEffect(() => {
    setIsRefreshingRemote(false);
    return () => {
      abortControllerRef.current?.abort();
      abortControllerRef.current = null;
    };
  }, [entryId]);

  const handleRefreshFromProvider = useCallback(async () => {
    if (!entry?.isConnected || abortControllerRef.current) {
      return;
    }

    const refreshProviderId = entry.provider || mainMediaProviderId;
    const abortController = new AbortController();
    abortControllerRef.current = abortController;
    setIsRefreshingRemote(true);

    try {
      await refreshEntryFromProvider(
        refreshProviderId,
        abortController.signal,
        reloadEntry,
        showSnackbar,
        'Could not refresh from this provider. Try again.',
        'Status refreshed from provider',
      );
    } finally {
      if (abortControllerRef.current === abortController) {
        abortControllerRef.current = null;
        setIsRefreshingRemote(false);
      }
    }
  }, [entry, reloadEntry, showSnackbar]);

  return { isRefreshingRemote, handleRefreshFromProvider };
}
