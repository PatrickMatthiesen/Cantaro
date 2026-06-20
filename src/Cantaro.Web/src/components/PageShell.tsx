import { Link, useRouterState } from '@tanstack/react-router';
import { Bell, Menu, Search, X } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
import { AppNavigation } from './AppNavigation';
import { useAuth } from '../contexts/AuthContext';

interface PageShellProps {
  children: ReactNode;
  sidebar: ReactNode;
  bottomSlot?: ReactNode;
  contentClassName?: string;
  searchPlaceholder?: string;
  searchValue?: string;
  onSearchChange?: (value: string) => void;
  onSearchSubmit?: () => void;
}

const drawerFocusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function getDrawerFocusableElements(drawer: HTMLDivElement | null): HTMLElement[] {
  return Array.from(drawer?.querySelectorAll<HTMLElement>(drawerFocusableSelector) ?? [])
    .filter((element) => element.offsetParent !== null);
}

function trapDrawerFocus(event: KeyboardEvent, drawer: HTMLDivElement | null) {
  const focusableElements = getDrawerFocusableElements(drawer);
  if (focusableElements.length === 0) return;

  const firstElement = focusableElements[0];
  const lastElement = focusableElements[focusableElements.length - 1];
  const shouldMoveToEnd = event.shiftKey && document.activeElement === firstElement;
  const shouldMoveToStart = !event.shiftKey && document.activeElement === lastElement;

  if (shouldMoveToEnd) {
    event.preventDefault();
    lastElement.focus();
  }

  if (shouldMoveToStart) {
    event.preventDefault();
    firstElement.focus();
  }
}

function handleDrawerKeyboard(event: KeyboardEvent, drawer: HTMLDivElement | null, onClose: () => void) {
  if (event.key === 'Escape') {
    event.preventDefault();
    onClose();
    return;
  }

  if (event.key === 'Tab') {
    trapDrawerFocus(event, drawer);
  }
}

function TopSearchInput({
  placeholder,
  value,
  onChange,
  onSubmit,
}: {
  placeholder: string;
  value?: string;
  onChange?: (value: string) => void;
  onSubmit?: () => void;
}) {
  return (
    <form
      className="min-w-0 flex-1"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit?.();
      }}
    >
      <label className="relative block">
        <Search className="pointer-events-none absolute top-1/2 left-4 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
        <input
          className="h-12 w-full rounded-2xl border border-[#e3def8] bg-white/70 pr-4 pl-11 text-sm font-medium text-slate-800 transition outline-none placeholder:text-slate-400 focus:border-violet-300 focus:bg-white"
          placeholder={placeholder}
          type="search"
          value={onChange ? value ?? '' : undefined}
          onChange={(event) => onChange?.(event.target.value)}
        />
      </label>
    </form>
  );
}

function NotificationButton() {
  return (
    <button
      type="button"
      className="flex h-11 w-11 items-center justify-center rounded-full text-slate-700 transition hover:bg-white/70"
      aria-label="Notifications"
    >
      <Bell className="h-5 w-5" aria-hidden />
    </button>
  );
}

function MobileMenuButton({ buttonRef, onClick }: { buttonRef: RefObject<HTMLButtonElement | null>; onClick: () => void }) {
  return (
    <button
      ref={buttonRef}
      type="button"
      className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl border border-[#e3def8] bg-white/70 text-slate-800 shadow-[0_12px_34px_rgba(88,74,150,0.08)] transition hover:bg-white lg:hidden"
      aria-label="Open navigation menu"
      onClick={onClick}
    >
      <Menu className="h-5 w-5" aria-hidden />
    </button>
  );
}

function ProfileButton({ userEmail, displayName, avatarUrl }: { userEmail?: string; displayName?: string; avatarUrl?: string }) {
  const userInitial = displayName?.trim().charAt(0).toUpperCase() || userEmail?.trim().charAt(0).toUpperCase() || 'C';

  return (
    <Link
      to="/settings"
      className="flex h-12 w-12 items-center justify-center overflow-hidden rounded-full bg-linear-to-br from-violet-500 to-slate-950 text-sm font-black text-white shadow-[0_14px_34px_rgba(88,74,150,0.22)] ring-2 ring-transparent transition hover:ring-violet-300 focus-visible:ring-violet-400 focus-visible:outline-none"
      aria-label={`Open settings for ${displayName || userEmail || 'profile'}`}
    >
      {avatarUrl ? <img src={avatarUrl} alt="" className="h-full w-full object-cover" /> : userInitial}
    </Link>
  );
}

function PageTopBar({
  pathname,
  searchPlaceholder,
  searchValue,
  onSearchChange,
  onSearchSubmit,
  userEmail,
  displayName,
  avatarUrl,
  onOpenNavigation,
  navigationButtonRef,
}: {
  pathname: string;
  searchPlaceholder: string;
  searchValue?: string;
  onSearchChange?: (value: string) => void;
  onSearchSubmit?: () => void;
  userEmail?: string;
  displayName?: string;
  avatarUrl?: string;
  onOpenNavigation: () => void;
  navigationButtonRef: RefObject<HTMLButtonElement | null>;
}) {
  return (
    <header className="sticky top-0 z-20 border-b border-white/80 bg-[#f7f5ff]/82 px-4 py-4 backdrop-blur-xl sm:px-8 lg:px-10">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:gap-4">
        <div className="flex min-w-0 items-center gap-3 lg:hidden">
          <MobileMenuButton buttonRef={navigationButtonRef} onClick={onOpenNavigation} />
          <AppNavigation pathname={pathname} />
        </div>
        <div className="hidden lg:block">
          <AppNavigation pathname={pathname} />
        </div>
        <div className="flex min-w-0 items-center gap-3 lg:flex-1">
          <TopSearchInput
            placeholder={searchPlaceholder}
            value={searchValue}
            onChange={onSearchChange}
            onSubmit={onSearchSubmit}
          />
          <div className="flex items-center gap-2">
            <NotificationButton />
            <ProfileButton userEmail={userEmail} displayName={displayName} avatarUrl={avatarUrl} />
          </div>
        </div>
      </div>
    </header>
  );
}

function MobileNavigationDrawer({
  isOpen,
  onClose,
  children,
}: {
  isOpen: boolean;
  onClose: () => void;
  children: ReactNode;
}) {
  const drawerRef = useRef<HTMLDivElement | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (!isOpen) return;

    closeButtonRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      handleDrawerKeyboard(event, drawerRef.current, onClose);
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 lg:hidden" role="dialog" aria-modal="true" aria-label="Navigation menu">
      <button
        type="button"
        className="absolute inset-0 bg-slate-950/35 backdrop-blur-sm"
        aria-label="Close navigation menu"
        onClick={onClose}
      />
      <div ref={drawerRef} className="relative h-full w-[min(86vw,22rem)] overflow-hidden rounded-r-[2rem] bg-[#f7f5ff] shadow-[24px_0_80px_rgba(15,23,42,0.24)]">
        <button
          ref={closeButtonRef}
          type="button"
          className="absolute top-4 right-4 z-10 flex h-10 w-10 items-center justify-center rounded-2xl border border-[#e3def8] bg-white/80 text-slate-700 shadow-[0_12px_34px_rgba(88,74,150,0.08)]"
          aria-label="Close navigation menu"
          onClick={onClose}
        >
          <X className="h-5 w-5" aria-hidden />
        </button>
        {children}
      </div>
    </div>
  );
}

export function PageShell({
  children,
  sidebar,
  bottomSlot,
  contentClassName = '',
  searchPlaceholder = 'Search Cantaro...',
  searchValue,
  onSearchChange,
  onSearchSubmit,
}: PageShellProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const { user } = useAuth();
  const [isMobileNavigationOpen, setIsMobileNavigationOpen] = useState(false);
  const mobileNavigationButtonRef = useRef<HTMLButtonElement | null>(null);

  const closeMobileNavigation = useCallback(() => {
    setIsMobileNavigationOpen(false);
    window.requestAnimationFrame(() => {
      mobileNavigationButtonRef.current?.focus();
    });
  }, []);

  useEffect(() => {
    setIsMobileNavigationOpen(false);
  }, [pathname]);

  return (
    <div className="min-h-screen bg-[#f7f5ff] text-slate-950">
      <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[272px_1fr]">
        <div className="hidden lg:block">{sidebar}</div>

        <div className="flex min-w-0 flex-col pb-28">
          <PageTopBar
            pathname={pathname}
            searchPlaceholder={searchPlaceholder}
            searchValue={searchValue}
            onSearchChange={onSearchChange}
            onSearchSubmit={onSearchSubmit}
            userEmail={user?.email}
            displayName={user?.displayName}
            avatarUrl={user?.avatarUrl}
            navigationButtonRef={mobileNavigationButtonRef}
            onOpenNavigation={() => setIsMobileNavigationOpen(true)}
          />

          <main className={`w-full px-4 py-6 sm:px-8 lg:px-10 ${contentClassName}`}>
            {children}
          </main>
        </div>
      </div>

      <MobileNavigationDrawer isOpen={isMobileNavigationOpen} onClose={closeMobileNavigation}>
        {sidebar}
      </MobileNavigationDrawer>

      {bottomSlot}
    </div>
  );
}
