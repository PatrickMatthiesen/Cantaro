import { GlassCard, GradientButton } from '../../../../Cantaro.Web/src/components/ui/GlassComponents';
import { DEFAULT_API_BASE_URL } from '../../../lib/extensionRuntimeConfig';

interface SetupCardProps {
  onOpenSettings: () => void;
  onSignIn: () => Promise<void>;
  isSigningIn: boolean;
}

export function SetupCard({ onOpenSettings, onSignIn, isSigningIn }: SetupCardProps) {
  return (
    <GlassCard className="mx-auto max-w-4xl p-8">
      <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro extension</p>
      <h2 className="mt-3 text-3xl font-bold text-gray-900">Connect the popup to your Cantaro instance</h2>
      <p className="mt-3 max-w-2xl text-sm text-gray-600">
        The media tab reuses the shared library UI from the web app, so it needs the Cantaro API URL and an extension session.
        The build currently defaults the API endpoint to <span className="font-semibold text-gray-800">{DEFAULT_API_BASE_URL}</span>, and
        extension pages cannot rely on your browser session cookie, so Cantaro signs the extension in with Authorization Code + PKCE.
      </p>
      <div className="mt-6 flex flex-wrap gap-3">
        <GradientButton tone="soft" onClick={() => void onSignIn()} disabled={isSigningIn}>
          {isSigningIn ? 'Opening browser…' : 'Sign in with browser'}
        </GradientButton>
        <GradientButton onClick={onOpenSettings}>Open settings</GradientButton>
      </div>
    </GlassCard>
  );
}