import { useEffect, useRef, useState } from 'react';

interface TurnstileApi {
  render: (container: HTMLElement, options: {
    sitekey: string;
    action: string;
    callback: (token: string) => void;
    'expired-callback': () => void;
    'error-callback': () => void;
  }) => string | number;
  reset: (widgetId?: string | number) => void;
  remove: (widgetId: string | number) => void;
}

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

interface TurnstileConfiguration {
  enabled: boolean;
  siteKey?: string;
}

let turnstileScriptPromise: Promise<void> | undefined;

function loadTurnstileScript(): Promise<void> {
  if (window.turnstile) {
    return Promise.resolve();
  }

  if (turnstileScriptPromise) {
    return turnstileScriptPromise;
  }

  turnstileScriptPromise = new Promise<void>((resolve, reject) => {
    const existingScript = document.querySelector<HTMLScriptElement>('script[data-cantaro-turnstile]');
    if (existingScript) {
      existingScript.addEventListener('load', () => resolve(), { once: true });
      existingScript.addEventListener('error', () => reject(new Error('Turnstile script failed to load.')), { once: true });
      return;
    }

    const script = document.createElement('script');
    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
    script.async = true;
    script.defer = true;
    script.dataset.cantaroTurnstile = 'true';
    script.addEventListener('load', () => resolve(), { once: true });
    script.addEventListener('error', () => reject(new Error('Turnstile script failed to load.')), { once: true });
    document.head.appendChild(script);
  });

  return turnstileScriptPromise;
}

export interface TurnstileWidgetProps {
  action: string;
  onConfigured: (enabled: boolean) => void;
  onToken: (token: string | null) => void;
  resetKey?: number;
}

function useTurnstileConfiguration(onConfigured: (enabled: boolean) => void) {
  const configuredCallbackRef = useRef(onConfigured);
  const [configuration, setConfiguration] = useState<TurnstileConfiguration | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    configuredCallbackRef.current = onConfigured;
  }, [onConfigured]);

  useEffect(() => {
    let cancelled = false;
    void fetch('/api/auth/turnstile', { credentials: 'include' })
      .then(async (response) => {
        if (!response.ok) throw new Error('Turnstile configuration could not be loaded.');
        return response.json() as Promise<TurnstileConfiguration>;
      })
      .then((value) => {
        if (cancelled) return;
        setConfiguration(value);
        configuredCallbackRef.current(value.enabled);
      })
      .catch(() => {
        if (cancelled) return;
        setError(true);
        configuredCallbackRef.current(true);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return { configuration, error };
}

function useTurnstileRenderer(
  configuration: TurnstileConfiguration | null,
  action: string,
  onToken: (token: string | null) => void,
  resetKey: number,
) {
  const containerRef = useRef<HTMLDivElement>(null);
  const widgetRef = useRef<string | number | undefined>(undefined);
  const tokenCallbackRef = useRef(onToken);
  const [error, setError] = useState(false);

  useEffect(() => {
    tokenCallbackRef.current = onToken;
  }, [onToken]);

  useEffect(() => {
    if (!configuration?.enabled || !configuration.siteKey || !containerRef.current) {
      return;
    }

    const siteKey = configuration.siteKey;

    let cancelled = false;
    void loadTurnstileScript()
      .then(() => {
        if (cancelled || !containerRef.current || !window.turnstile) return;
        widgetRef.current = window.turnstile.render(containerRef.current, {
          sitekey: siteKey,
          action,
          callback: (token) => tokenCallbackRef.current(token),
          'expired-callback': () => tokenCallbackRef.current(null),
          'error-callback': () => tokenCallbackRef.current(null),
        });
      })
      .catch(() => {
        if (cancelled) return;
        setError(true);
        tokenCallbackRef.current(null);
      });

    return () => {
      cancelled = true;
      if (widgetRef.current !== undefined) {
        window.turnstile?.remove(widgetRef.current);
        widgetRef.current = undefined;
      }
    };
  }, [action, configuration]);

  useEffect(() => {
    if (resetKey > 0 && window.turnstile && widgetRef.current !== undefined) {
      window.turnstile.reset(widgetRef.current);
      tokenCallbackRef.current(null);
    }
  }, [resetKey]);

  return { containerRef, error };
}

export function TurnstileWidget({ action, onConfigured, onToken, resetKey = 0 }: TurnstileWidgetProps) {
  const { configuration, error: configurationError } = useTurnstileConfiguration(onConfigured);
  const { containerRef, error: rendererError } = useTurnstileRenderer(configuration, action, onToken, resetKey);
  const error = configurationError || rendererError;

  if (configuration?.enabled) {
    return (
      <div className="space-y-2" aria-live="polite">
        <div ref={containerRef} />
        {error ? <p className="text-sm text-danger-content">Bot protection could not load. Try again.</p> : null}
      </div>
    );
  }

  if (error) {
    return <p className="text-sm text-danger-content">Bot protection could not be checked. Try again.</p>;
  }

  return null;
}
