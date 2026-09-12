import { useState } from 'react';
import { authApi } from '@cantaro/client-shared/auth';
import { Trash2 } from 'lucide-react';
import { useSignInMethods } from '../hooks/useSignInMethods';
import { useGoogleAccountStatus } from '../hooks/useGoogleAccountStatus';

// fallow-ignore-next-line complexity
export function DeleteAccountPanel() {
  const { methods, error: configError } = useSignInMethods();
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const { status, error: statusError } = useGoogleAccountStatus(methods?.googleEnabled === true);

  const usePassword = methods?.localLoginEnabled && (!methods.googleEnabled || status?.hasPassword);
  const canDelete = usePassword ? !!password : status?.linked && status.reauthenticated;

  // fallow-ignore-next-line complexity
  async function confirmGoogle() {
    setBusy(true);
    setError(null);
    try {
      const response = await fetch('/api/auth/google/reauth', {
        method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ returnUrl: '/settings#data' }),
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.message || result.error || 'Google confirmation failed.');
      const target = new URL(result.authorizationUrl, window.location.origin);
      if (target.origin !== window.location.origin) throw new Error('Invalid confirmation address.');
      window.location.assign(target.href);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Google confirmation failed.');
      setBusy(false);
    }
  }

  // fallow-ignore-next-line complexity
  async function deleteAccount() {
    if (!window.confirm('Permanently delete your Cantaro account? This cannot be undone.')) return;
    setBusy(true);
    setError(null);
    try {
      if (usePassword) {
        await authApi.deleteAccount(password);
      } else {
        const response = await fetch('/api/profile', {
          method: 'DELETE', credentials: 'include',
          headers: { 'Content-Type': 'application/json', 'X-Cantaro-Confirm-Delete': 'true' },
          body: JSON.stringify({ useGoogleReauthentication: true }),
        });
        if (!response.ok) {
          const result = await response.json();
          throw new Error(result.error || 'Account deletion failed.');
        }
      }
      window.location.assign('/');
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Account deletion failed.');
      setBusy(false);
    }
  }

  return (
    <div className="border border-danger-border bg-danger-surface p-5">
      <strong className="text-sm text-danger-content">Delete account</strong>
      <p className="mt-1 text-xs leading-5 text-danger-content">This permanently removes your profile, libraries, connections, and settings.</p>
      {usePassword ? <input aria-label="Current password for account deletion" type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} placeholder="Current password" className="mt-3 min-h-10 w-full border border-danger-border bg-surface px-3 text-sm text-content outline-none" /> : null}
      {!usePassword && status?.linked ? <button type="button" disabled={busy} onClick={() => void confirmGoogle()} className="mt-3 min-h-10 border border-danger-border px-4 text-sm text-danger-content">{status.reauthenticated ? 'Confirm with Google again' : 'Confirm identity with Google'}</button> : null}
      <button type="button" disabled={!canDelete || busy} onClick={() => void deleteAccount()} className="mt-3 inline-flex min-h-10 items-center gap-2 bg-danger-action px-4 text-xs font-bold text-danger-action-content transition-colors hover:bg-danger-action-hover disabled:opacity-50"><Trash2 className="h-4 w-4" />Delete permanently</button>
      {error || configError || statusError ? <p role="alert" className="mt-2 text-xs font-bold text-danger-content">{error || configError || statusError}</p> : null}
    </div>
  );
}
