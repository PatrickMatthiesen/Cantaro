import { BlurredEmail } from '@cantaro/client-shared/auth';
import { GradientButton } from '@cantaro/client-shared/ui';
import type { FormEvent } from 'react';
import { DEFAULT_BASE_URL } from '../../platform/settings/extensionSettings';
import type { SettingsController } from './useSettings';

interface SettingsPageProps {
  controller: SettingsController;
  onClose: () => void;
}

export function SettingsPage({ controller, onClose }: SettingsPageProps) {
  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (await controller.save()) onClose();
  };

  return (
    <section className="mx-auto w-full max-w-2xl p-5" aria-labelledby="settings-title">
      <header className="flex items-start justify-between gap-4 border-b border-border-subtle pb-4">
        <div>
          <h1 id="settings-title" className="text-xl font-bold text-content">Extension settings</h1>
          <p className="mt-1 text-sm text-content-muted">Connection, page features, and diagnostics.</p>
        </div>
        <button
          type="button"
          className="min-h-9 rounded-xl px-3 text-sm font-semibold text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          onClick={onClose}
        >
          Close
        </button>
      </header>

      <form className="divide-y divide-border-subtle" onSubmit={(event) => void handleSubmit(event)}>
        <ConnectionSettings controller={controller} />
        <PageFeatureSettings controller={controller} />
        <DiagnosticsSettings controller={controller} />
        <SettingsActions controller={controller} onClose={onClose} />
      </form>
    </section>
  );
}

function ConnectionSettings({ controller }: { controller: SettingsController }) {
  return (
    <section className="space-y-4 py-5" aria-labelledby="connection-settings-title">
      <div>
        <h2 id="connection-settings-title" className="text-sm font-bold text-content">Cantaro connection</h2>
        <p className="mt-1 text-xs leading-5 text-content-muted">The extension uses its own secure session instead of your website cookie.</p>
      </div>

      <SessionRow controller={controller} />

      <div className="grid grid-cols-2 gap-3">
        <UrlField
          id="baseUrl"
          label="Base URL"
          detail={`Build default: ${DEFAULT_BASE_URL}`}
          placeholder={DEFAULT_BASE_URL}
          value={controller.draft.baseUrl}
          disabled={controller.loading}
          onChange={(value) => controller.updateDraft('baseUrl', value)}
        />
      </div>
    </section>
  );
}

function SessionRow({ controller }: { controller: SettingsController }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-xl bg-surface-subtle p-3">
      <div className="min-w-0">
        <p className="text-sm font-semibold text-content">Browser authentication</p>
        <p className="mt-0.5 truncate text-xs text-content-muted"><SessionDescription controller={controller} /></p>
      </div>
      <SessionActions controller={controller} />
    </div>
  );
}

function SessionDescription({ controller }: { controller: SettingsController }) {
  if (controller.isCheckingSession) return <>Checking session…</>;
  if (!controller.sessionEmail) return <>Not signed in</>;
  return <>Signed in as <BlurredEmail email={controller.sessionEmail} blur={controller.blurEmailAddress} /></>;
}

function SessionActions({ controller }: { controller: SettingsController }) {
  return (
    <div className="flex shrink-0 gap-2">
      {controller.sessionEmail ? (
        <GradientButton type="button" tone="soft" onClick={() => void controller.disconnect()} disabled={controller.loading || controller.isDisconnecting}>
          {controller.isDisconnecting ? 'Signing out…' : 'Sign out'}
        </GradientButton>
      ) : null}
      <GradientButton type="button" onClick={() => void controller.signIn()} disabled={controller.loading || controller.isSigningIn}>
        <SignInLabel controller={controller} />
      </GradientButton>
    </div>
  );
}

function SignInLabel({ controller }: { controller: SettingsController }) {
  if (controller.isSigningIn) return <>Opening browser…</>;
  return <>{controller.sessionEmail ? 'Re-authenticate' : 'Sign in'}</>;
}

function PageFeatureSettings({ controller }: { controller: SettingsController }) {
  return (
    <section className="py-5" aria-labelledby="page-features-title">
      <h2 id="page-features-title" className="text-sm font-bold text-content">Page features</h2>
      <SettingToggle
        label="Show lyrics on YouTube"
        detail="Add a Cantaro lyrics panel to recognised YouTube and YouTube Music song pages."
        checked={controller.draft.injectLyricsOnYouTube}
        disabled={controller.loading}
        onChange={(checked) => controller.updateDraft('injectLyricsOnYouTube', checked)}
      />
    </section>
  );
}

function DiagnosticsSettings({ controller }: { controller: SettingsController }) {
  return (
    <section className="py-5" aria-labelledby="diagnostics-title">
      <h2 id="diagnostics-title" className="text-sm font-bold text-content">Diagnostics</h2>
      <SettingToggle
        label="Verbose logging"
        detail="Log collector scans, extracted URLs, duplicate suppression, and delivery results. Warnings and errors remain enabled."
        checked={controller.draft.verboseLogging}
        disabled={controller.loading}
        onChange={(checked) => controller.updateDraft('verboseLogging', checked)}
      />
    </section>
  );
}

function SettingsActions({ controller, onClose }: { controller: SettingsController; onClose: () => void }) {
  return (
    <footer className="flex items-center justify-between gap-3 py-4">
      <p className="text-xs text-content-muted">
        {controller.hasUnsavedChanges ? 'You have unsaved changes.' : 'Settings are up to date.'}
      </p>
      <div className="flex gap-2">
        <GradientButton type="button" tone="soft" onClick={onClose}>Cancel</GradientButton>
        <GradientButton type="submit" disabled={controller.loading || !controller.hasUnsavedChanges}>Save changes</GradientButton>
      </div>
    </footer>
  );
}

function UrlField({ id, label, detail, value, placeholder, disabled, onChange }: {
  id: string;
  label: string;
  detail: string;
  value: string;
  placeholder?: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label htmlFor={id} className="block text-xs font-semibold text-content">
      {label}
      <input
        id={id}
        type="url"
        value={value}
        placeholder={placeholder}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        className="mt-2 w-full rounded-xl border border-border-subtle bg-surface px-3 py-2.5 text-sm font-normal text-content outline-none transition-colors placeholder:text-content-muted focus:border-focus focus:ring-2 focus:ring-accent-soft disabled:cursor-not-allowed disabled:opacity-60"
      />
      <span className="mt-1.5 block font-normal leading-5 text-content-muted">{detail}</span>
    </label>
  );
}

function SettingToggle({ label, detail, checked, disabled, onChange }: {
  label: string;
  detail: string;
  checked: boolean;
  disabled: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label className="mt-3 flex cursor-pointer items-start justify-between gap-4 rounded-xl py-2 focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-focus">
      <span>
        <span className="block text-sm font-semibold text-content">{label}</span>
        <span className="mt-1 block max-w-xl text-xs leading-5 text-content-muted">{detail}</span>
      </span>
      <span className="relative mt-0.5 inline-flex shrink-0 items-center">
        <input
          type="checkbox"
          className="peer sr-only"
          checked={checked}
          disabled={disabled}
          onChange={(event) => onChange(event.target.checked)}
        />
        <span className="h-6 w-11 rounded-full bg-border-strong transition-colors peer-checked:bg-accent peer-disabled:cursor-not-allowed peer-disabled:opacity-60 after:absolute after:top-0.5 after:left-0.5 after:size-5 after:rounded-full after:bg-surface after:transition-transform peer-checked:after:translate-x-5" aria-hidden />
      </span>
    </label>
  );
}
