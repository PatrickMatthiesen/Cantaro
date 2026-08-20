import { useState, type ReactNode } from 'react';
import { LoginForm } from './LoginForm';
import { RegisterForm } from './RegisterForm';
import { useAuth } from '../contexts/AuthContext';

export interface GlobalHeadingState {
  eyebrow: string;
  title: string;
  details?: string[];
  hidden?: boolean;
}

function AppLoadingState() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-canvas text-content">
      <div className="flex items-center gap-3 border-y border-border-subtle px-6 py-4">
        <span className="h-3 w-3 animate-pulse rounded-full bg-personal-accent" aria-hidden />
        <p className="text-sm font-medium">Loading…</p>
      </div>
    </div>
  );
}

function LandingPage() {
  const [showRegister, setShowRegister] = useState(false);

  return (
    <div className="min-h-screen bg-canvas text-content">
      <div className="mx-auto flex min-h-screen max-w-6xl flex-col px-6 pt-8 pb-16">
        <header className="mb-8 border-b border-border-subtle pb-6">
          <p className="text-xs tracking-[0.35em] text-personal-accent-strong uppercase">Cantaro</p>
          <h1 className="mt-1 text-3xl font-black tracking-tight text-content">Music and media library</h1>
        </header>

        <main className="grid flex-1 gap-6 lg:grid-cols-[1.1fr_0.9fr]">
          <section className="border-y border-border-subtle py-8 lg:pr-12">
            <h2 className="text-2xl font-semibold text-content">Get started</h2>
            <p className="mt-2 text-sm text-content-muted">
              Connect services, sync playlists, and review conflicts when a match needs confirmation.
            </p>
            <ol className="mt-6 space-y-3">
              {[
                'Create an account and sign in.',
                'Connect your first music service. YouTube is available now, with more platforms coming later.',
                'Run sync and manage playlist updates from Cantaro.',
              ].map((item, index) => (
                <li key={item} className="flex items-start gap-3 border-t border-border-subtle py-4 first:border-0">
                  <span className="flex h-7 w-7 shrink-0 items-center justify-center bg-personal-accent text-xs font-semibold text-personal-accent-content">
                    {index + 1}
                  </span>
                  <span className="text-sm text-content-muted">{item}</span>
                </li>
              ))}
            </ol>
          </section>

          <section className="border-y border-border-subtle py-8 lg:pl-12">
            {showRegister ? (
              <RegisterForm onSwitchToLogin={() => setShowRegister(false)} />
            ) : (
              <LoginForm onSwitchToRegister={() => setShowRegister(true)} />
            )}
          </section>
        </main>
      </div>
    </div>
  );
}

export function RequireAuth({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <AppLoadingState />;
  }

  if (!isAuthenticated) {
    return <LandingPage />;
  }

  return <>{children}</>;
}
