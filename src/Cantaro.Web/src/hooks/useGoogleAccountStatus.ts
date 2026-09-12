import { useEffect, useState } from 'react';

interface GoogleAccountStatus {
  linked: boolean;
  hasPassword: boolean;
  reauthenticated?: boolean;
}

export function useGoogleAccountStatus(enabled: boolean) {
  const [status, setStatus] = useState<GoogleAccountStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    void fetch('/api/auth/google/status', { credentials: 'include', signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('Could not check account security. Reload to try again.');
        setStatus(await response.json());
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not check account security.');
      });
    return () => controller.abort();
  }, [enabled]);

  return { status, error };
}
