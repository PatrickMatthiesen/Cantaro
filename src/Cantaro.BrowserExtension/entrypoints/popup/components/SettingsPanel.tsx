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
      <div className="mx-auto max-w-lg space-y-5 rounded-xl bg-surface p-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs tracking-[0.28em] text-content-muted uppercase">Settings</p>
            <h2 className="mt-2 text-2xl font-bold text-content">Extension connection</h2>
          </div>
          <div>
            <label htmlFor="webBaseUrl" className="text-xs font-medium tracking-[0.24em] text-content-muted uppercase">Website URL</label>
            <input id="webBaseUrl" type="url" value={webBaseUrl} onChange={(event) => onWebBaseUrlChange(event.target.value)} disabled={loading} className="mt-2 w-full rounded-2xl border border-border-subtle bg-surface px-4 py-3 text-sm text-content outline-none transition focus:border-focus focus:ring-2 focus:ring-accent-soft" />
            <p className="mt-2 text-xs text-content-muted">Used by “Open in Cantaro”. Set this to your self-hosted web app.</p>
          </div>
          <button
            type="button"
            className="rounded-full bg-surface-translucent px-3 py-2 text-sm font-semibold text-content-muted transition hover:bg-surface-hover"
            onClick={onClose}
          >
            Close
          </button>
        </div>

        <form className="mt-6 flex flex-col gap-5" onSubmit={(event) => void onSubmit(event)}>
          <section className="rounded-xl border border-border-subtle bg-surface-subtle p-4">
            <h3 className="text-sm font-semibold text-content">Appearance</h3>
            <p className="mt-1 text-xs leading-5 text-content-muted">
              The extension follows your Cantaro theme preference, including system, light, and dark mode. Change it on Cantaro’s main settings page.
            </p>
          </section>
          <section className="rounded-xl border border-border-subtle bg-surface-subtle p-4">
            <div className="flex items-start justify-between gap-4">
              <div>
                <h3 className="text-sm font-semibold text-content">Show lyrics on YouTube</h3>
                <p className="mt-1 text-xs leading-5 text-content-muted">
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
                <span className="h-6 w-11 rounded-full bg-border-strong transition peer-checked:bg-accent peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus peer-disabled:cursor-not-allowed peer-disabled:opacity-60 after:absolute after:top-0.5 after:left-0.5 after:size-5 after:rounded-full after:bg-surface after:transition peer-checked:after:translate-x-5" aria-hidden />
                <span className="sr-only">Show lyrics on YouTube</span>
              </label>
            </div>
          </section>
          <div>
            <div className="flex items-center justify-between gap-3">
              <p className="text-xs font-medium tracking-[0.24em] text-content-muted uppercase">Browser auth session</p>
              <span className={`rounded-full px-3 py-1 text-[11px] font-semibold ${sessionEmail
                ? 'bg-success-surface text-success-content'
                : 'bg-surface-subtle text-content-muted'}`}>
                {isCheckingSession ? 'Checking session…' : sessionEmail ? <span>Signed in as <BlurredEmail email={sessionEmail} blur={blurEmailAddress} /></span> : 'No verified session'}
              </span>
            </div>
            <div className="mt-3 rounded-xl border border-border-subtle bg-surface-subtle p-4">
              <p className="text-sm text-content-muted">
                Sign in opens a browser auth window, uses the Cantaro API cookie session, and returns a PKCE-bound authorization code to the extension.
              </p>
              <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
                <p className="text-xs text-content-muted">Refresh tokens stay server-managed and rotate on every refresh.</p>
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
            <label htmlFor="apiBaseUrl" className="text-xs font-medium tracking-[0.24em] text-content-muted uppercase">
              API base URL
            </label>
            <input
              id="apiBaseUrl"
              type="url"
              placeholder={defaultApiBaseUrl}
              value={apiBaseUrl}
              onChange={(event) => onApiBaseUrlChange(event.target.value)}
              disabled={loading}
              className="mt-2 w-full rounded-2xl border border-border-subtle bg-surface-translucent px-4 py-3 text-sm text-content outline-none transition focus:border-focus focus:ring-2 focus:ring-accent-soft"
            />
            <p className="mt-2 text-xs text-content-muted">
              Point this at your Cantaro API, not the extension background service. The build default is {defaultApiBaseUrl}.
            </p>
          </div>

          <div className="mt-auto flex items-center justify-between gap-3">
            <p className="text-xs text-content-muted">{hasUnsavedChanges ? 'Unsaved changes' : 'Saved values are already loaded'}</p>
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
