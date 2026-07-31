import { GlassCard, GradientButton } from '@cantaro/client-shared/ui';
import { DEFAULT_API_BASE_URL } from '../../../lib/extensionRuntimeConfig';

interface SetupCardProps {
  onOpenSettings: () => void;
  onSignIn: () => Promise<void>;
  isSigningIn: boolean;
}

export function SetupCard({ onOpenSettings, onSignIn, isSigningIn }: SetupCardProps) {
  return (
    <GlassCard className="mx-auto max-w-4xl p-5">
      <p className="text-xs font-semibold text-accent-strong">Cantaro extension</p>
      <h2 className="mt-1 text-xl font-bold text-content">Connect to your Cantaro instance</h2>
      <p className="mt-2 max-w-2xl text-sm text-content-muted">
        The media tab reuses the shared client library UI, so it needs the Cantaro API URL and an extension session.
        The build currently defaults the API endpoint to <span className="font-semibold text-content">{DEFAULT_API_BASE_URL}</span>, and
        extension pages cannot rely on your browser session cookie, so Cantaro signs the extension in with Authorization Code + PKCE.
      </p>
      <div className="mt-4 flex flex-wrap gap-2">
        <GradientButton tone="soft" onClick={() => void onSignIn()} disabled={isSigningIn}>
          {isSigningIn ? 'Opening browser…' : 'Sign in with browser'}
        </GradientButton>
        <GradientButton onClick={onOpenSettings}>Open settings</GradientButton>
      </div>
    </GlassCard>
  );
}
