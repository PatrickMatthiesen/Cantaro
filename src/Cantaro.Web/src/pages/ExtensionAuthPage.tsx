import type { FormEvent } from 'react';
import { useEffect, useState } from 'react';
import { GlassCard, GradientButton } from '../components/ui/GlassComponents';

declare const __CANTARO_TRUSTED_API_BASE_URL__: string;

type AuthPhase = 'checking' | 'ready' | 'submitting';

interface AuthTarget {
  apiBaseUrl: string;
  returnTo: string;
}

function normalizeApiBaseUrl(value: string): string {
  return value.trim().replace(/\/+$/, '');
}

function readTrustedApiBaseUrl(): string {
  const configuredBaseUrl = __CANTARO_TRUSTED_API_BASE_URL__?.trim() ?? '';

  if (!configuredBaseUrl) {
    return window.location.origin;
  }

  try {
    return normalizeApiBaseUrl(new URL(configuredBaseUrl).origin);
  } catch {
    return window.location.origin;
  }
}

function readErrorMessage(payload: unknown, fallbackMessage: string): string {
  if (!payload || typeof payload !== 'object') {
    return fallbackMessage;
  }

  const record = payload as Record<string, unknown>;
  const candidates = [record.message, record.error_description, record.error, record.detail, record.title];
  const firstMessage = candidates.find((value) => typeof value === 'string' && value.trim());
  return typeof firstMessage === 'string' ? firstMessage : fallbackMessage;
}

function readAuthTarget(): AuthTarget | null {
  const returnTo = new URLSearchParams(window.location.search).get('returnTo')?.trim() ?? '';
  if (!returnTo) {
    return null;
  }

  const trustedApiBaseUrl = readTrustedApiBaseUrl();

  try {
    const targetUrl = new URL(returnTo);
    const targetApiBaseUrl = normalizeApiBaseUrl(targetUrl.origin);
    if (targetUrl.pathname !== '/api/auth/extension/authorize' || targetApiBaseUrl !== trustedApiBaseUrl) {
      return null;
    }

    return {
      apiBaseUrl: trustedApiBaseUrl,
      returnTo: targetUrl.toString(),
    };
  } catch {
    return null;
  }
}

export function ExtensionAuthPage() {
  const [phase, setPhase] = useState<AuthPhase>('checking');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const authTarget = readAuthTarget();

  useEffect(() => {
    if (!authTarget) {
      setPhase('ready');
      setError('The extension sign-in request was missing a valid return target.');
      return;
    }

    let cancelled = false;

    const continueIfSessionExists = async () => {
      try {
        const response = await fetch(`${authTarget.apiBaseUrl}/api/auth/me`, {
          method: 'GET',
          credentials: 'include',
        });

        if (response.ok) {
          window.location.assign(authTarget.returnTo);
          return;
        }
      } catch {
        // Ignore and fall through to the login form.
      }

      if (!cancelled) {
        setPhase('ready');
      }
    };

    void continueIfSessionExists();

    return () => {
      cancelled = true;
    };
  }, [authTarget]);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (!authTarget) {
      setError('The extension sign-in request could not be resumed.');
      return;
    }

    if (!email.trim() || !password) {
      setError('Enter your Cantaro email and password to continue.');
      return;
    }

    setPhase('submitting');
    setError(null);

    try {
      const response = await fetch(`${authTarget.apiBaseUrl}/api/login?useCookies=true`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        credentials: 'include',
        body: JSON.stringify({
          email: email.trim(),
          password,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(readErrorMessage(payload, 'Sign-in failed.'));
      }

      window.location.assign(authTarget.returnTo);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in failed.');
      setPhase('ready');
    }
  };

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-slate-100 via-cyan-50 to-white text-gray-900">
      <div className="absolute -top-24 -left-24 h-80 w-80 rounded-full bg-cyan-300/35 blur-3xl" aria-hidden />
      <div className="absolute right-0 bottom-0 h-96 w-96 rounded-full bg-orange-200/30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto flex min-h-screen max-w-4xl items-center justify-center px-6 py-10">
        <GlassCard className="w-full max-w-xl p-8">
          <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro extension auth</p>
          <h1 className="mt-3 text-3xl font-bold text-gray-900">Finish sign-in for the browser extension</h1>
          <p className="mt-3 text-sm text-gray-600">
            This page signs you into the Cantaro API origin, then hands control back to the extension’s PKCE authorization flow.
          </p>

          {authTarget ? (
            <p className="mt-4 rounded-2xl bg-white/75 px-4 py-3 text-xs text-gray-500">
              API origin: <span className="font-semibold text-gray-800">{authTarget.apiBaseUrl}</span>
            </p>
          ) : null}

          {phase === 'checking' ? (
            <div className="mt-8 rounded-2xl border border-white/80 bg-white/70 px-4 py-5 text-sm text-gray-600">
              Checking whether the API session is already active…
            </div>
          ) : (
            <form className="mt-8 space-y-4" onSubmit={(event) => void handleSubmit(event)}>
              <div>
                <label htmlFor="extension-auth-email" className="text-xs font-medium tracking-[0.24em] text-gray-500 uppercase">
                  Email
                </label>
                <input
                  id="extension-auth-email"
                  type="email"
                  autoComplete="email"
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  disabled={phase === 'submitting' || !authTarget}
                  className="mt-2 w-full rounded-2xl border border-white/70 bg-white/85 px-4 py-3 text-sm text-gray-900 transition outline-none focus:border-cyan-400 focus:ring-2 focus:ring-cyan-300/70"
                  placeholder="you@example.com"
                />
              </div>

              <div>
                <label htmlFor="extension-auth-password" className="text-xs font-medium tracking-[0.24em] text-gray-500 uppercase">
                  Password
                </label>
                <input
                  id="extension-auth-password"
                  type="password"
                  autoComplete="current-password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  disabled={phase === 'submitting' || !authTarget}
                  className="mt-2 w-full rounded-2xl border border-white/70 bg-white/85 px-4 py-3 text-sm text-gray-900 transition outline-none focus:border-cyan-400 focus:ring-2 focus:ring-cyan-300/70"
                  placeholder="Your Cantaro password"
                />
              </div>

              {error ? (
                <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                  {error}
                </div>
              ) : null}

              <div className="flex flex-wrap items-center justify-between gap-3 pt-2">
                <p className="text-xs text-gray-500">
                  The API cookie stays on the Cantaro API origin. The extension only receives the final authorization code and tokens.
                </p>
                <GradientButton type="submit" disabled={phase === 'submitting' || !authTarget}>
                  {phase === 'submitting' ? 'Signing in…' : 'Continue to extension'}
                </GradientButton>
              </div>
            </form>
          )}
        </GlassCard>
      </div>
    </div>
  );
}