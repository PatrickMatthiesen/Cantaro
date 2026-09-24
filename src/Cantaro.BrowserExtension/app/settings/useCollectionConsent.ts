import { useCallback, useEffect, useRef, useState } from 'react';
import type { ConsentStatus } from '../../platform/consent/consentService';
import {
  getRuntimeConsentStatus,
  saveRuntimeConsent,
  revokeRuntimeConsent,
  subscribeRuntimeConsent,
} from '../../platform/consent/runtimeConsentClient';
import type { CollectionChoices } from './CollectionConsent';

export function useCollectionConsent(baseUrl: string, configured: boolean) {
  const [status, setStatus] = useState<ConsentStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const revision = useRef(0);
  const operation = useRef(0);
  const currentBaseUrl = useRef(baseUrl);

  useEffect(() => {
    currentBaseUrl.current = baseUrl;
    revision.current++;
    operation.current++;
    setStatus(null);
    setLoading(true);
    setBusy(false);
    setError(null);
  }, [baseUrl]);

  useEffect(() => {
    let active = true;
    const stop = subscribeRuntimeConsent(next => {
      revision.current++;
      if (active) { setStatus(next); setLoading(false); setError(null); }
    });
    const initialRevision = revision.current;
    void getRuntimeConsentStatus(baseUrl).then(next => {
      if (active && revision.current === initialRevision) setStatus(next);
    }).catch(() => {
      if (active && revision.current === initialRevision) setError('Could not load collection choices. Reopen the popup to retry; collection stays off.');
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; stop(); };
  }, [baseUrl, configured]);

  const save = useCallback(async (choices: CollectionChoices) => {
    // A sign-in can finish after the user has switched Cantaro servers.
    // Consent belongs to the server shown when they chose to enable it.
    if (currentBaseUrl.current !== baseUrl) return;
    const saveRevision = ++revision.current;
    const saveOperation = ++operation.current;
    setBusy(true);
    setError(null);
    try {
      const next = await saveRuntimeConsent(baseUrl, choices);
      if (revision.current === saveRevision) setStatus(next);
    } catch {
      if (operation.current === saveOperation) setError('Could not save collection choices. Check your Cantaro connection and try again.');
    } finally {
      if (operation.current === saveOperation) setBusy(false);
    }
  }, [baseUrl]);

  const reset = useCallback(async () => {
    const resetRevision = ++revision.current;
    const resetOperation = ++operation.current;
    setBusy(true);
    setError(null);
    try {
      await revokeRuntimeConsent(baseUrl);
      if (revision.current === resetRevision) setStatus(null);
    } catch {
      if (operation.current === resetOperation) setError('Could not reset collection choices. Try again.');
    } finally { if (operation.current === resetOperation) setBusy(false); }
  }, [baseUrl]);

  return {
    status,
    loading,
    busy,
    error,
    save,
    reset,
    revoke: () => save({ watchTracking: false, catalogCollection: false, musicLyrics: false }),
  };
}
