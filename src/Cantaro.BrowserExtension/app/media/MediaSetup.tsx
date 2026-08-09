import { GradientButton } from '@cantaro/client-shared/ui';

interface MediaSetupProps {
  isSigningIn: boolean;
  onSignIn: () => Promise<boolean>;
  onOpenSettings: () => void;
}

export function MediaSetup({ isSigningIn, onSignIn, onOpenSettings }: MediaSetupProps) {
  return (
    <section className="mx-auto flex h-full max-w-xl flex-col justify-center p-8 text-center">
      <h1 className="text-xl font-bold text-content">Connect Cantaro to browse media</h1>
      <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-content-muted">
        Sign in to load your media library, watched progress, and collected provider links.
      </p>
      <div className="mt-5 flex justify-center gap-2">
        <GradientButton tone="soft" onClick={onOpenSettings}>Connection settings</GradientButton>
        <GradientButton onClick={() => void onSignIn()} disabled={isSigningIn}>
          {isSigningIn ? 'Opening browser…' : 'Sign in'}
        </GradientButton>
      </div>
    </section>
  );
}
