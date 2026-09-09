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

  useEffect(() => {
    let active = true;
    revision.current++;
    setStatus(null);
    setLoading(true);
    setBusy(false);
    setError(null);
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
    return () => { active = false; revision.current++; stop(); };
  }, [baseUrl, configured]);

  const save = useCallback(async (choices: CollectionChoices) => {
    const saveRevision = ++revision.current;
    setBusy(true);
    setError(null);
    try {
      const next = await saveRuntimeConsent(baseUrl, choices);
      if (revision.current === saveRevision) setStatus(next);
    } catch {
      if (revision.current === saveRevision) setError('Could not save collection choices. Check your Cantaro connection and try again.');
    } finally {
      setBusy(false);
    }
  }, [baseUrl]);

  const reset = useCallback(async () => {
    const resetRevision = ++revision.current;
    setBusy(true);
    setError(null);
    try {
      await revokeRuntimeConsent(baseUrl);
      if (revision.current === resetRevision) setStatus(null);
    } catch {
      if (revision.current === resetRevision) setError('Could not reset collection choices. Try again.');
    } finally { setBusy(false); }
  }, [baseUrl]);

  return {
    status,
    loading,
    busy,
    error,
    save,
    reset,
    revoke: () => save({ watchTracking: false, catalogCollection: false }),
  };
}
