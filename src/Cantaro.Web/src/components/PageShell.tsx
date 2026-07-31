import { Link, useRouterState } from '@tanstack/react-router';
import { Bell, LogOut, Menu, Settings, X } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
import { AppNavigation } from './AppNavigation';
import { rememberActiveArea } from '../appAreaRouting';
import { useAuth } from '../contexts/AuthContext';
import { GlobalSearch } from '../search/GlobalSearch';

interface PageShellProps {
  children: ReactNode;
  sidebar: ReactNode;
  bottomSlot?: ReactNode;
  contentClassName?: string;
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

function NotificationButton() {
  return (
    <button
      type="button"
      className="flex h-10 w-10 items-center justify-center rounded-full text-content-muted transition hover:bg-surface-translucent sm:h-11 sm:w-11"
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
      className="app-mobile-menu-button flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl border border-border-subtle bg-surface-translucent text-content shadow-[0_12px_34px_rgba(88,74,150,0.08)] transition hover:bg-surface focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none sm:h-11 sm:w-11 lg:hidden"
      aria-label="Open navigation menu"
      onClick={onClick}
    >
      <Menu className="h-5 w-5" aria-hidden />
    </button>
  );
}

function AccountMenu({
  displayName,
  avatarUrl,
  onLogout,
}: {
  displayName?: string;
  avatarUrl?: string;
  onLogout: () => void;
}) {
  const [isOpen, setIsOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const userInitial = displayName?.trim().charAt(0).toUpperCase() || 'C';
  const accountLabel = displayName || 'Account';

  useEffect(() => {
    if (!isOpen) return;

    const handlePointerDown = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setIsOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen]);

  return (
    <div ref={menuRef} className="relative">
      <button
        type="button"
        className="flex h-11 w-11 items-center justify-center overflow-hidden rounded-full bg-linear-to-br from-violet-500 to-slate-950 text-sm font-black text-content-inverse shadow-[0_14px_34px_rgba(88,74,150,0.22)] ring-2 ring-transparent transition hover:ring-violet-300 focus-visible:ring-focus focus-visible:outline-none sm:h-12 sm:w-12"
        aria-label={`Open account menu for ${accountLabel}`}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        onClick={() => setIsOpen((current) => !current)}
      >
        {avatarUrl ? <img src={avatarUrl} alt="" className="h-full w-full object-cover" /> : userInitial}
      </button>

      {isOpen ? (
        <div
          role="menu"
          className="absolute top-full right-0 z-40 mt-3 w-64 overflow-hidden rounded-2xl border border-border-subtle bg-surface/96 p-2 text-content shadow-[0_20px_70px_rgba(88,74,150,0.18)] backdrop-blur-xl"
        >
          <div className="px-3 py-3">
            <p className="truncate text-sm font-black text-content">{displayName || 'Cantaro account'}</p>
            <p className="mt-0.5 text-xs font-semibold text-content-muted">Personal archive controls</p>
          </div>
          <div className="h-px bg-border-subtle" />
          <Link
            to="/settings"
            role="menuitem"
            className="mt-2 flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-bold text-content-muted transition hover:bg-canvas hover:text-content focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none"
            onClick={() => setIsOpen(false)}
          >
            <Settings className="h-4 w-4 text-violet-600" aria-hidden />
            Settings
          </Link>
          <button
            type="button"
            role="menuitem"
            className="flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-bold text-content-muted transition hover:bg-danger-surface hover:text-danger-content focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none"
            onClick={() => {
              setIsOpen(false);
              onLogout();
            }}
          >
            <LogOut className="h-4 w-4" aria-hidden />
            Log out
          </button>
        </div>
      ) : null}
    </div>
  );
}

function PageTopBar({
  pathname,
  displayName,
  avatarUrl,
  onLogout,
  onOpenNavigation,
  navigationButtonRef,
}: {
  pathname: string;
  displayName?: string;
  avatarUrl?: string;
  onLogout: () => void;
  onOpenNavigation: () => void;
  navigationButtonRef: RefObject<HTMLButtonElement | null>;
}) {
  return (
    <header className="app-top-bar sticky top-0 z-20 border-b border-border-subtle bg-canvas/82 px-4 py-3 backdrop-blur-xl sm:px-8 sm:py-4 lg:px-10">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:gap-4">
        <div className="flex min-w-0 items-center justify-between gap-3 lg:contents">
          <div className="flex min-w-0 items-center gap-2 sm:gap-3">
            <MobileMenuButton buttonRef={navigationButtonRef} onClick={onOpenNavigation} />
            <AppNavigation pathname={pathname} />
          </div>
          <div className="flex shrink-0 items-center gap-1.5 sm:gap-2 lg:order-3">
            <NotificationButton />
            <AccountMenu displayName={displayName} avatarUrl={avatarUrl} onLogout={onLogout} />
          </div>
        </div>
        <GlobalSearch />
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
      <div ref={drawerRef} className="relative h-full w-[min(86vw,22rem)] overflow-hidden rounded-r-[2rem] bg-canvas shadow-[24px_0_80px_rgba(15,23,42,0.24)]">
        <button
          ref={closeButtonRef}
          type="button"
          className="absolute top-4 right-4 z-10 flex h-10 w-10 items-center justify-center rounded-2xl border border-border-subtle bg-surface/80 text-content-muted shadow-[0_12px_34px_rgba(88,74,150,0.08)]"
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
}: PageShellProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const { user, logout } = useAuth();
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
    rememberActiveArea(pathname);
  }, [pathname]);

  return (
    <div className="min-h-screen bg-canvas text-content">
      <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[272px_1fr]">
        <div className="hidden lg:block">{sidebar}</div>

        <div className="flex min-w-0 flex-col pb-28">
          <PageTopBar
            pathname={pathname}
            displayName={user?.displayName}
            avatarUrl={user?.avatarUrl}
            onLogout={() => void logout()}
            navigationButtonRef={mobileNavigationButtonRef}
            onOpenNavigation={() => setIsMobileNavigationOpen(true)}
          />

          <main className={`w-full px-3 py-5 sm:px-8 sm:py-6 lg:px-10 ${contentClassName}`}>
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
