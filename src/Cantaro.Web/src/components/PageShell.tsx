import { useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { AppNavigation } from './AppNavigation';
import { useAuth } from '../contexts/AuthContext';

interface PageShellProps {
  children: ReactNode;
  sidebar: ReactNode;
  bottomSlot?: ReactNode;
  contentClassName?: string;
  searchPlaceholder?: string;
}

function TopSearchInput({ placeholder }: { placeholder: string }) {
  return (
    <div className="min-w-[220px] flex-1">
      <label className="relative block">
        <svg
          className="pointer-events-none absolute top-1/2 left-4 h-4 w-4 -translate-y-1/2 text-slate-400"
          viewBox="0 0 24 24"
          fill="none"
          aria-hidden
        >
          <path
            d="m21 21-4.35-4.35M10.5 18a7.5 7.5 0 1 1 0-15 7.5 7.5 0 0 1 0 15Z"
            stroke="currentColor"
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth="2"
          />
        </svg>
        <input
          className="h-12 w-full rounded-2xl border border-[#e3def8] bg-white/70 pr-4 pl-11 text-sm font-medium text-slate-800 transition outline-none placeholder:text-slate-400 focus:border-violet-300 focus:bg-white"
          placeholder={placeholder}
          type="search"
        />
      </label>
    </div>
  );
}

function NotificationButton() {
  return (
    <button
      type="button"
      className="flex h-11 w-11 items-center justify-center rounded-full text-slate-700 transition hover:bg-white/70"
      aria-label="Notifications"
    >
      <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" aria-hidden>
        <path
          d="M18 8.8a6 6 0 0 0-12 0c0 7.2-3 7.2-3 9.2h18c0-2-3-2-3-9.2Z"
          stroke="currentColor"
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="1.8"
        />
        <path
          d="M9.8 21a2.4 2.4 0 0 0 4.4 0"
          stroke="currentColor"
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="1.8"
        />
      </svg>
    </button>
  );
}

function ProfileButton({ userEmail }: { userEmail?: string }) {
  const userInitial = userEmail?.trim().charAt(0).toUpperCase() || 'C';

  return (
    <button
      type="button"
      className="flex h-12 w-12 items-center justify-center rounded-full bg-linear-to-br from-violet-500 to-slate-950 text-sm font-black text-white shadow-[0_14px_34px_rgba(88,74,150,0.22)]"
      aria-label="Profile"
    >
      {userInitial}
    </button>
  );
}

function PageTopBar({
  pathname,
  searchPlaceholder,
  userEmail,
}: {
  pathname: string;
  searchPlaceholder: string;
  userEmail?: string;
}) {
  return (
    <header className="sticky top-0 z-20 border-b border-white/80 bg-[#f7f5ff]/82 px-4 py-4 backdrop-blur-xl sm:px-8 lg:px-10">
      <div className="flex flex-wrap items-center gap-4">
        <AppNavigation pathname={pathname} />
        <TopSearchInput placeholder={searchPlaceholder} />
        <NotificationButton />
        <ProfileButton userEmail={userEmail} />
      </div>
    </header>
  );
}

export function PageShell({
  children,
  sidebar,
  bottomSlot,
  contentClassName = '',
  searchPlaceholder = 'Search Cantaro...',
}: PageShellProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const { user } = useAuth();

  return (
    <div className="min-h-screen bg-[#f7f5ff] text-slate-950">
      <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[272px_1fr]">
        {sidebar}

        <div className="flex min-w-0 flex-col pb-28">
          <PageTopBar pathname={pathname} searchPlaceholder={searchPlaceholder} userEmail={user?.email} />

          <main className={`w-full px-4 py-6 sm:px-8 lg:px-10 ${contentClassName}`}>
            {children}
          </main>
        </div>
      </div>

      {bottomSlot}
    </div>
  );
}
