import type { FormEvent } from 'react';
import { GradientButton } from '@cantaro/client-shared/ui';
import { BlurredEmail } from '@cantaro/client-shared/auth';

interface SettingsPanelProps {
  apiBaseUrl: string;
  webBaseUrl: string;
  loading: boolean;
  isSigningIn: boolean;
  isDisconnecting: boolean;
  isCheckingSession: boolean;
  hasUnsavedChanges: boolean;
  sessionEmail: string | null;
  blurEmailAddress: boolean;
  injectLyricsOnYouTube: boolean;
  defaultApiBaseUrl: string;
  onClose: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  onSignIn: () => Promise<void>;
  onDisconnect: () => Promise<void>;
  onApiBaseUrlChange: (value: string) => void;
  onWebBaseUrlChange: (value: string) => void;
  onInjectLyricsOnYouTubeChange: (value: boolean) => void;
}

// fallow-ignore-next-line complexity
export function SettingsPanel({
  apiBaseUrl,
  webBaseUrl,
  loading,
  isSigningIn,
  isDisconnecting,
  isCheckingSession,
  hasUnsavedChanges,
  sessionEmail,
  blurEmailAddress,
  injectLyricsOnYouTube,
  defaultApiBaseUrl,
  onClose,
  onSubmit,
  onSignIn,
  onDisconnect,
  onApiBaseUrlChange,
  onWebBaseUrlChange,
  onInjectLyricsOnYouTubeChange,
}: SettingsPanelProps) {
  return (
    <div className="p-3">
      <div className="mx-auto max-w-lg space-y-5 rounded-xl bg-white p-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs tracking-[0.28em] text-gray-500 uppercase">Settings</p>
            <h2 className="mt-2 text-2xl font-bold text-gray-900">Extension connection</h2>
          </div>
          <div>
            <label htmlFor="webBaseUrl" className="text-xs font-medium tracking-[0.24em] text-gray-500 uppercase">Website URL</label>
            <input id="webBaseUrl" type="url" value={webBaseUrl} onChange={(event) => onWebBaseUrlChange(event.target.value)} disabled={loading} className="mt-2 w-full rounded-2xl border border-violet-100 bg-white px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-violet-500 focus:ring-2 focus:ring-violet-200" />
            <p className="mt-2 text-xs text-gray-500">Used by “Open in Cantaro”. Set this to your self-hosted web app.</p>
          </div>
          <button
            type="button"
            className="rounded-full bg-white/70 px-3 py-2 text-sm font-semibold text-gray-700 transition hover:bg-white"
            onClick={onClose}
          >
            Close
          </button>
        </div>

        <form className="mt-6 flex flex-col gap-5" onSubmit={(event) => void onSubmit(event)}>
          <section className="rounded-xl border border-violet-100 bg-violet-50/40 p-4">
            <h3 className="text-sm font-semibold text-slate-900">Appearance</h3>
            <p className="mt-1 text-xs leading-5 text-slate-600">
              The extension follows your Cantaro theme preference, including system, light, and dark mode. Change it on Cantaro’s main settings page.
            </p>
          </section>
          <section className="rounded-xl border border-violet-100 bg-violet-50/40 p-4">
            <div className="flex items-start justify-between gap-4">
              <div>
                <h3 className="text-sm font-semibold text-slate-900">Show lyrics on YouTube</h3>
                <p className="mt-1 text-xs leading-5 text-slate-600">
                  Add a Cantaro panel to recognised song pages on YouTube and YouTube Music. Lyrics stay off unless you enable this.
                </p>
              </div>
              <label className="relative mt-0.5 inline-flex shrink-0 cursor-pointer items-center">
                <input
                  type="checkbox"
                  className="peer sr-only"
                  checked={injectLyricsOnYouTube}
                  onChange={(event) => onInjectLyricsOnYouTubeChange(event.target.checked)}
                  disabled={loading}
                />
                <span className="h-6 w-11 rounded-full bg-slate-300 transition peer-checked:bg-violet-600 peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-violet-600 peer-disabled:cursor-not-allowed peer-disabled:opacity-60 after:absolute after:top-0.5 after:left-0.5 after:size-5 after:rounded-full after:bg-white after:transition peer-checked:after:translate-x-5" aria-hidden />
                <span className="sr-only">Show lyrics on YouTube</span>
              </label>
            </div>
          </section>
          <div>
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs font-medium tracking-[0.24em] text-gray-500 uppercase">Browser auth session</p>
              <span className={`rounded-full px-3 py-1 text-[11px] font-semibold ${sessionEmail
                ? 'bg-emerald-100 text-emerald-700'
                : 'bg-slate-100 text-slate-600'}`}>
                {isCheckingSession ? 'Checking session…' : sessionEmail ? <span>Signed in as <BlurredEmail email={sessionEmail} blur={blurEmailAddress} /></span> : 'No verified session'}
              </span>
            </div>
            <div className="mt-3 rounded-xl border border-violet-100 bg-violet-50/40 p-4">
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
      </div>
    </div>
  );
}
