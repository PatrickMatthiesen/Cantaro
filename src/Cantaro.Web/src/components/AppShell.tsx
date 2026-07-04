import { useState, type ReactNode } from 'react';
import { GlassCard } from '@cantaro/client-shared/ui';
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
    <div className="flex min-h-screen items-center justify-center bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-800">
      <GlassCard className="flex items-center gap-3 px-6 py-4">
        <span className="h-3 w-3 animate-pulse rounded-full bg-indigo-500" aria-hidden />
        <p className="text-sm font-medium">Loading…</p>
      </GlassCard>
    </div>
  );
}

function LandingPage() {
  const [showRegister, setShowRegister] = useState(false);

  return (
    <div className="app-gradient-shell relative min-h-screen overflow-hidden text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto flex min-h-screen max-w-6xl flex-col px-6 pt-8 pb-16">
        <header className="mb-8">
          <p className="text-xs tracking-[0.35em] text-gray-500 uppercase">Cantaro</p>
          <h1 className="mt-1 bg-linear-to-r from-indigo-600 to-pink-600 bg-clip-text text-3xl font-bold text-transparent">
            Music and media library
          </h1>
        </header>

        <main className="grid flex-1 gap-6 lg:grid-cols-[1.1fr_0.9fr]">
          <GlassCard className="p-8">
            <h2 className="text-2xl font-semibold text-gray-900">Get started</h2>
            <p className="mt-2 text-sm text-gray-600">
              Connect services, sync playlists, and review conflicts when a match needs confirmation.
            </p>
            <ol className="mt-6 space-y-3">
              {[
                'Create an account and sign in.',
                'Connect your first music service. YouTube is available now, with more platforms coming later.',
                'Run sync and manage playlist updates from Cantaro.',
              ].map((item, index) => (
                <li key={item} className="flex items-start gap-3 rounded-2xl bg-white/70 p-4">
                  <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-linear-to-r from-indigo-500 to-purple-500 text-xs font-semibold text-white">
                    {index + 1}
                  </span>
                  <span className="text-sm text-gray-700">{item}</span>
                </li>
              ))}
            </ol>
          </GlassCard>

          <GlassCard className="p-8">
            {showRegister ? (
              <RegisterForm onSwitchToLogin={() => setShowRegister(false)} />
            ) : (
              <LoginForm onSwitchToRegister={() => setShowRegister(true)} />
            )}
          </GlassCard>
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
