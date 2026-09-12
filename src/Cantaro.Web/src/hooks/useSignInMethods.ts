import { useEffect, useState } from 'react';

interface SignInMethods {
  localLoginEnabled: boolean;
  googleEnabled: boolean;
}

export function useSignInMethods() {
  const [methods, setMethods] = useState<SignInMethods | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void fetch('/api/auth/methods', { credentials: 'include', signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error('Could not load sign-in options. Reload this page to try again.');
        const result: SignInMethods = await response.json();
        if (typeof result.localLoginEnabled !== 'boolean' || typeof result.googleEnabled !== 'boolean') {
          throw new Error('The server returned invalid sign-in options.');
        }
        setMethods(result);
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Could not load sign-in options.');
      });
    return () => controller.abort();
  }, []);

  return { methods, error };
}
