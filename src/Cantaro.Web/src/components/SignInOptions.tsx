import type { ReactNode } from 'react';
import { useSignInMethods } from '../hooks/useSignInMethods';

// fallow-ignore-next-line complexity
export function SignInOptions({ children }: { children: ReactNode }) {
  const { methods, error } = useSignInMethods();
  if (error) return <p role="alert" className="text-sm text-danger-content">{error}</p>;
  if (!methods) return <p role="status" className="text-sm text-content-muted">Loading sign-in options…</p>;

  const returnLocation = new URL(window.location.href);
  returnLocation.searchParams.delete('authError');
  returnLocation.searchParams.delete('authSuccess');
  const returnUrl = returnLocation.pathname + returnLocation.search + returnLocation.hash;
  const authError = new URLSearchParams(window.location.search).get('authError');
  return (
    <div className="space-y-6">
      {authError ? <p role="alert" className="text-sm text-danger-content">{authError === 'google_email_collision'
        ? 'A Cantaro account already uses this email. Sign in with your Cantaro password and link Google in Settings. If password sign-in is disabled, contact your server administrator to enable it for migration.'
        : 'Google sign-in could not be completed. Try again or contact your server administrator.'}</p> : null}
      {methods.googleEnabled ? (
        <div className="space-y-3">
          {!methods.localLoginEnabled ? <h2 className="text-2xl font-semibold text-content">Sign in to Cantaro</h2> : null}
          <a href={`/api/auth/google/login?returnUrl=${encodeURIComponent(returnUrl)}`} className="inline-flex min-h-12 w-full items-center justify-center border border-border-strong bg-surface px-4 font-semibold text-content hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus">Sign in with Google</a>
          {!methods.localLoginEnabled ? <p className="text-sm text-content-muted">Use your Google account to sign in or create a Cantaro account.</p> : null}
        </div>
      ) : null}
      {methods.localLoginEnabled ? children : null}
    </div>
  );
}
