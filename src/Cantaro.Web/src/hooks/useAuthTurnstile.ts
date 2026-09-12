import { useState, type FormEvent } from 'react';

import type { TurnstileWidgetProps } from '@cantaro/client-shared/auth';

export function useAuthTurnstile(action: string) {
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [resetKey, setResetKey] = useState(0);

  const guardSubmission = (event: FormEvent<HTMLFormElement>, setError: (message: string) => void) => {
    if (enabled === false || token) return false;
    event.preventDefault();
    setError(enabled === null ? 'Checking bot protection. Try again in a moment.' : 'Complete the bot check to continue.');
    return true;
  };

  const reset = () => {
    setToken(null);
    setResetKey((key) => key + 1);
  };

  const widgetProps: TurnstileWidgetProps = {
    action,
    onConfigured: setEnabled,
    onToken: setToken,
    resetKey,
  };

  return { token, guardSubmission, reset, widgetProps };
}
