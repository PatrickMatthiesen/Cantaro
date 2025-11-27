import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';

interface UserProfileProps {
  className?: string;
}

export const UserProfile = ({ className = '' }: UserProfileProps) => {
  const { user, logout } = useAuth();
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  if (!user) {
    return null;
  }

  const handleLogout = async () => {
    setIsLoggingOut(true);
    try {
      await logout();
    } catch (error) {
      console.error('Logout error:', error);
    } finally {
      setIsLoggingOut(false);
    }
  };

  const memberSince = new Date(user.createdAt).toLocaleDateString();

  const baseClass = 'glass-panel text-white';
  const containerClass = [baseClass, className].filter(Boolean).join(' ');

  return (
    <div className={containerClass}>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-xs uppercase tracking-[0.5em] text-slate-400">Account</p>
          <h2 className="mt-2 text-2xl font-semibold">{user.email}</h2>
          <p className="mt-1 text-sm text-slate-400">ID: {user.id}</p>
        </div>
        <button
          onClick={handleLogout}
          disabled={isLoggingOut}
          className="inline-flex items-center gap-2 rounded-full border border-white/15 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-white transition hover:border-white/40 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isLoggingOut ? 'Signing out…' : 'Logout'}
        </button>
      </div>

      <dl className="mt-8 grid gap-6 sm:grid-cols-3">
        <div className="rounded-2xl border border-white/10 bg-white/5 p-5">
          <dt className="text-xs uppercase tracking-[0.5em] text-slate-400">Member since</dt>
          <dd className="mt-3 text-lg font-semibold text-white">{memberSince}</dd>
          <p className="mt-1 text-sm text-slate-400">Session managed via secure cookies.</p>
        </div>
        <div className="rounded-2xl border border-white/10 bg-white/5 p-5">
          <dt className="text-xs uppercase tracking-[0.5em] text-slate-400">Tokens</dt>
          <dd className="mt-3 text-lg font-semibold text-white">Encrypted</dd>
          <p className="mt-1 text-sm text-slate-400">Stored server-side only.</p>
        </div>
        <div className="rounded-2xl border border-white/10 bg-white/5 p-5">
          <dt className="text-xs uppercase tracking-[0.5em] text-slate-400">Adapters</dt>
          <dd className="mt-3 text-lg font-semibold text-white">Isolated</dd>
          <p className="mt-1 text-sm text-slate-400">Third-party logic stays sandboxed.</p>
        </div>
      </dl>
    </div>
  );
};
