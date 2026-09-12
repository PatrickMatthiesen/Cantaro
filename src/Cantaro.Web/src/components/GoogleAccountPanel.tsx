import { useState, type FormEvent } from 'react';
import { useSignInMethods } from '../hooks/useSignInMethods';
import { useGoogleAccountStatus } from '../hooks/useGoogleAccountStatus';

// fallow-ignore-next-line complexity
export function GoogleAccountPanel() {
  const { methods, error: configurationError } = useSignInMethods();
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const callbackError = new URLSearchParams(window.location.search).get('authError');
  const { status, error: statusError } = useGoogleAccountStatus(methods?.googleEnabled === true);

  // fallow-ignore-next-line complexity
  async function linkAccount(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const response = await fetch('/api/auth/google/link', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword: password, returnUrl: '/settings#security' }),
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.message || result.error || 'Could not link Google.');
      const target = new URL(result.authorizationUrl, window.location.origin);
      if (target.origin !== window.location.origin) throw new Error('The server returned an invalid sign-in address.');
      window.location.assign(target.href);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not link Google.');
      setBusy(false);
    }
  }

  if (configurationError || statusError) return <p role="alert" className="text-sm text-danger-content">{configurationError || statusError}</p>;
  if (!methods?.googleEnabled) return null;
  return (
    <div className="mb-6 space-y-3">
      <h3 className="text-lg font-semibold text-content">Google sign-in</h3>
      {callbackError ? <p role="alert" className="text-sm text-danger-content">Google could not be linked. Confirm your Cantaro password and choose a Google account that is not linked to another Cantaro account.</p> : null}
      {status?.linked ? <p className="text-sm text-content-muted">Google is linked to this Cantaro account. Your library stays with this account when you sign in with Google.</p> : null}
      {status && !status.linked && status.hasPassword ? (
        <form onSubmit={event => void linkAccount(event)} className="max-w-md space-y-3">
          <p className="text-sm text-content-muted">Confirm your Cantaro password, then choose the Google account you want to use for sign-in.</p>
          <label className="block text-sm text-content" htmlFor="google-link-password">Current Cantaro password</label>
          <input id="google-link-password" type="password" required autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} className="min-h-11 w-full border border-border-strong bg-surface px-4 text-content focus-visible:outline-2 focus-visible:outline-focus" />
          <button type="submit" disabled={busy} className="min-h-11 border border-border-strong bg-surface px-4 font-semibold text-content hover:bg-surface-hover disabled:opacity-60">{busy ? 'Opening Google…' : 'Link Google account'}</button>
        </form>
      ) : null}
      {!status && !error ? <p role="status" className="text-sm text-content-muted">Checking Google sign-in…</p> : null}
      {error ? <p role="alert" className="text-sm text-danger-content">{error}</p> : null}
    </div>
  );
}
