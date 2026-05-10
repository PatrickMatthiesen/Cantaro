import type { FormEvent } from 'react';
import { GlassCard, GradientButton } from '../cantaroWebUi';

interface SettingsPanelProps {
  apiBaseUrl: string;
  loading: boolean;
  isSigningIn: boolean;
  isDisconnecting: boolean;
  isCheckingSession: boolean;
  hasUnsavedChanges: boolean;
  sessionEmail: string | null;
  defaultApiBaseUrl: string;
  onClose: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  onSignIn: () => Promise<void>;
  onDisconnect: () => Promise<void>;
  onApiBaseUrlChange: (value: string) => void;
}

// fallow-ignore-next-line complexity
export function SettingsPanel({
  apiBaseUrl,
  loading,
  isSigningIn,
  isDisconnecting,
  isCheckingSession,
  hasUnsavedChanges,
  sessionEmail,
  defaultApiBaseUrl,
  onClose,
  onSubmit,
  onSignIn,
  onDisconnect,
  onApiBaseUrlChange,
}: SettingsPanelProps) {
  return (
    <div className="pointer-events-none absolute inset-0 z-40 flex justify-end bg-slate-950/30 p-4 backdrop-blur-sm">
      <GlassCard className="pointer-events-auto flex h-full w-full max-w-md flex-col p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs tracking-[0.28em] text-gray-500 uppercase">Settings</p>
            <h2 className="mt-2 text-2xl font-bold text-gray-900">Extension connection</h2>
          </div>
          <button
            type="button"
            className="rounded-full bg-white/70 px-3 py-2 text-sm font-semibold text-gray-700 transition hover:bg-white"
            onClick={onClose}
          >
            Close
          </button>
        </div>

        <form className="mt-6 flex flex-1 flex-col gap-5" onSubmit={(event) => void onSubmit(event)}>
          <div>
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs font-medium tracking-[0.24em] text-gray-500 uppercase">Browser auth session</p>
              <span className={`rounded-full px-3 py-1 text-[11px] font-semibold ${sessionEmail
                ? 'bg-emerald-100 text-emerald-700'
                : 'bg-slate-100 text-slate-600'}`}>
                {isCheckingSession ? 'Checking session…' : sessionEmail ? `Signed in as ${sessionEmail}` : 'No verified session'}
              </span>
            </div>
            <div className="mt-3 rounded-2xl border border-white/70 bg-white/65 p-4">
              <p className="text-sm text-gray-700">
                Sign in opens a browser auth window, uses the Cantaro API cookie session, and returns a PKCE-bound authorization code to the extension.
              </p>
              <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
                <p className="text-xs text-gray-500">Refresh tokens stay server-managed and rotate on every refresh.</p>
                <div className="flex flex-wrap gap-2">
                  {sessionEmail ? (
                    <GradientButton type="button" tone="soft" onClick={() => void onDisconnect()} disabled={loading || isDisconnecting}>
                      {isDisconnecting ? 'Clearing…' : 'Sign out'}
                    </GradientButton>
                  ) : null}
                  <GradientButton type="button" onClick={() => void onSignIn()} disabled={loading || isSigningIn}>
                    {isSigningIn ? 'Opening browser…' : sessionEmail ? 'Re-authenticate' : 'Sign in with browser'}
                  </GradientButton>
                </div>
              </div>
            </div>
          </div>

          <div>
            <label htmlFor="apiBaseUrl" className="text-xs font-medium tracking-[0.24em] text-gray-500 uppercase">
              API base URL
            </label>
            <input
              id="apiBaseUrl"
              type="url"
              placeholder={defaultApiBaseUrl}
              value={apiBaseUrl}
              onChange={(event) => onApiBaseUrlChange(event.target.value)}
              disabled={loading}
              className="mt-2 w-full rounded-2xl border border-white/70 bg-white/75 px-4 py-3 text-sm text-gray-900 outline-none transition focus:border-cyan-400 focus:ring-2 focus:ring-cyan-300/70"
            />
            <p className="mt-2 text-xs text-gray-500">
              Point this at your Cantaro API, not the extension background service. The build default is {defaultApiBaseUrl}.
            </p>
          </div>

          <div className="mt-auto flex items-center justify-between gap-3">
            <p className="text-xs text-gray-500">{hasUnsavedChanges ? 'Unsaved changes' : 'Saved values are already loaded'}</p>
            <div className="flex gap-2">
              <GradientButton type="button" tone="soft" onClick={onClose}>Dismiss</GradientButton>
              <GradientButton type="submit" disabled={loading}>Save</GradientButton>
            </div>
          </div>
        </form>
      </GlassCard>
    </div>
  );
}
