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

  return (
    <section
      className={`rounded-3xl border border-white/80 bg-white/70 p-6 text-gray-900 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px] ${className}`}
    >
      <p className="text-xs tracking-[0.28em] text-gray-500 uppercase">Account</p>
      <h2 className="mt-2 text-2xl font-semibold">{user.email}</h2>
      <p className="mt-1 text-sm text-gray-500">Member since {memberSince}</p>

      <div className="mt-5 rounded-2xl bg-white/70 p-4 text-sm text-gray-600">
        Cantaro stores service refresh tokens encrypted server-side and never in the browser.
      </div>

      <div className="mt-5 flex items-center justify-end">
        <button
          onClick={handleLogout}
          disabled={isLoggingOut}
          className="rounded-xl bg-gray-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isLoggingOut ? 'Signing out…' : 'Sign out'}
        </button>
      </div>
    </section>
  );
};
