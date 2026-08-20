import { Link, useRouterState } from "@tanstack/react-router";
import {
  Bell,
  LogOut,
  Menu,
  PanelLeftClose,
  PanelLeftOpen,
  Search,
  Settings,
  X,
} from "lucide-react";
import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type ReactNode,
  type RefObject,
} from "react";
import { AppNavigation } from "./AppNavigation";
import { rememberActiveArea } from "../appAreaRouting";
import { useAuth } from "../contexts/AuthContext";
import { GlobalSearch } from "../search/GlobalSearch";

interface PageShellProps {
  children: ReactNode;
  sidebar: ReactNode;
  bottomSlot?: ReactNode;
  contentClassName?: string;
}

const drawerFocusableSelector =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function getDrawerFocusableElements(
  drawer: HTMLDivElement | null,
): HTMLElement[] {
  return Array.from(
    drawer?.querySelectorAll<HTMLElement>(drawerFocusableSelector) ?? [],
  ).filter((element) => element.offsetParent !== null);
}

function trapDrawerFocus(event: KeyboardEvent, drawer: HTMLDivElement | null) {
  const focusableElements = getDrawerFocusableElements(drawer);
  if (focusableElements.length === 0) return;

  const firstElement = focusableElements[0];
  const lastElement = focusableElements[focusableElements.length - 1];
  const shouldMoveToEnd =
    event.shiftKey && document.activeElement === firstElement;
  const shouldMoveToStart =
    !event.shiftKey && document.activeElement === lastElement;

  if (shouldMoveToEnd) {
    event.preventDefault();
    lastElement.focus();
  }

  if (shouldMoveToStart) {
    event.preventDefault();
    firstElement.focus();
  }
}

function handleDrawerKeyboard(
  event: KeyboardEvent,
  drawer: HTMLDivElement | null,
  onClose: () => void,
) {
  if (event.key === "Escape") {
    event.preventDefault();
    onClose();
    return;
  }

  if (event.key === "Tab") {
    trapDrawerFocus(event, drawer);
  }
}

function NotificationButton() {
  return (
    <button
      type="button"
      className="flex h-11 w-11 items-center justify-center text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus"
      aria-label="Notifications"
    >
      <Bell className="h-5 w-5" aria-hidden />
    </button>
  );
}

function MobileMenuButton({
  buttonRef,
  onClick,
}: {
  buttonRef: RefObject<HTMLButtonElement | null>;
  onClick: () => void;
}) {
  return (
    <button
      ref={buttonRef}
      type="button"
      className="flex h-11 w-11 shrink-0 items-center justify-center border border-border-subtle bg-surface text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus sm:hidden"
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
  const userInitial = displayName?.trim().charAt(0).toUpperCase() || "C";
  const accountLabel = displayName || "Account";

  useEffect(() => {
    if (!isOpen) return;

    const handlePointerDown = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsOpen(false);
      }
    };

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen]);

  return (
    <div ref={menuRef} className="relative">
      <button
        type="button"
        className="flex h-11 w-11 items-center justify-center overflow-hidden rounded-full bg-content text-sm font-black text-canvas ring-2 ring-transparent transition hover:ring-personal-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        aria-label={`Open account menu for ${accountLabel}`}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        onClick={() => setIsOpen((current) => !current)}
      >
        {avatarUrl ? (
          <img src={avatarUrl} alt="" className="h-full w-full object-cover" />
        ) : (
          userInitial
        )}
      </button>

      {isOpen ? (
        <div
          role="menu"
          className="absolute top-full right-0 z-40 mt-3 w-64 border border-border-subtle bg-surface p-2 text-content shadow-xl"
        >
          <div className="px-3 py-3">
            <p className="truncate text-sm font-black text-content">
              {displayName || "Cantaro account"}
            </p>
            <p className="mt-0.5 text-xs font-semibold text-content-muted">
              Personal archive controls
            </p>
          </div>
          <div className="h-px bg-border-subtle" />
          <Link
            to="/settings"
            role="menuitem"
            className="mt-2 flex min-h-11 w-full items-center gap-3 px-3 py-2.5 text-sm font-bold text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus"
            onClick={() => setIsOpen(false)}
          >
            <Settings className="h-4 w-4" aria-hidden />
            Settings
          </Link>
          <button
            type="button"
            role="menuitem"
            className="flex min-h-11 w-full items-center gap-3 px-3 py-2.5 text-left text-sm font-bold text-content-muted transition-colors hover:bg-danger-surface hover:text-danger-content focus-visible:outline-2 focus-visible:outline-focus"
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
    <header className="sticky top-0 z-30 border-b border-border-subtle bg-canvas/90 px-3 py-3 backdrop-blur-xl sm:px-5 lg:px-8">
      <div className="flex min-w-0 items-center gap-2 sm:gap-3">
        <MobileMenuButton
          buttonRef={navigationButtonRef}
          onClick={onOpenNavigation}
        />
        <div className="shrink-0">
          <AppNavigation pathname={pathname} />
        </div>
        <div className="hidden min-w-0 flex-1 md:block">
          <GlobalSearch />
        </div>
        <div className="ml-auto flex shrink-0 items-center gap-1">
          <Link
            to="/search"
            search={{ group: "all", preview: true }}
            className="flex h-11 w-11 items-center justify-center text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus md:hidden"
            aria-label="Search Cantaro"
          >
            <Search className="h-5 w-5" aria-hidden />
          </Link>
          <NotificationButton />
          <AccountMenu
            displayName={displayName}
            avatarUrl={avatarUrl}
            onLogout={onLogout}
          />
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

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) {
    return null;
  }

  return (
    <div
      className="fixed inset-0 z-50 lg:hidden"
      role="dialog"
      aria-modal="true"
      aria-label="Navigation menu"
    >
      <button
        type="button"
        className="absolute inset-0 bg-slate-950/35 backdrop-blur-sm"
        aria-label="Close navigation menu"
        onClick={onClose}
      />
      <div
        ref={drawerRef}
        className="group/navigation-drawer relative h-full w-[min(86vw,22rem)] overflow-hidden bg-canvas shadow-2xl"
        data-navigation-drawer="expanded"
      >
        <button
          ref={closeButtonRef}
          type="button"
          className="absolute top-4 right-4 z-10 flex h-11 w-11 items-center justify-center border border-border-subtle bg-surface text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus"
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

function readSidebarFoldedPreference() {
  if (typeof window === "undefined") return false;
  return window.localStorage.getItem("cantaro:sidebar-folded") === "true";
}

function DesktopSidebar({
  sidebar,
  isFolded,
  onToggle,
  onOpenCompact,
  compactButtonRef,
}: {
  sidebar: ReactNode;
  isFolded: boolean;
  onToggle: () => void;
  onOpenCompact: () => void;
  compactButtonRef: RefObject<HTMLButtonElement | null>;
}) {
  const label = isFolded ? "Expand sidebar" : "Fold sidebar";
  return (
    <div className="relative hidden sm:block">
      {sidebar}
      <button
        ref={compactButtonRef}
        type="button"
        className="fixed bottom-4 left-4 z-20 hidden h-11 w-11 items-center justify-center border border-border-subtle bg-canvas text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus sm:flex lg:hidden"
        aria-label="Expand sidebar"
        title="Expand sidebar"
        onClick={onOpenCompact}
      >
        <PanelLeftOpen className="h-5 w-5" aria-hidden />
      </button>
      <button
        type="button"
        className="fixed bottom-4 left-3 z-20 hidden h-11 items-center gap-3 border border-border-subtle bg-canvas px-3 text-sm font-semibold text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-focus lg:flex"
        aria-label={label}
        title={label}
        onClick={onToggle}
      >
        {isFolded ? (
          <PanelLeftOpen className="h-5 w-5" aria-hidden />
        ) : (
          <PanelLeftClose className="h-5 w-5" aria-hidden />
        )}
        <span className="group-data-[sidebar=compact]/sidebar:hidden">
          Fold sidebar
        </span>
      </button>
    </div>
  );
}

function getSidebarGridClassName(isFolded: boolean) {
  const desktopColumns = isFolded
    ? "lg:grid-cols-[76px_1fr]"
    : "lg:grid-cols-[248px_1fr]";
  return `group/sidebar grid min-h-screen grid-cols-1 sm:grid-cols-[76px_1fr] ${desktopColumns}`;
}

export function PageShell({
  children,
  sidebar,
  bottomSlot,
  contentClassName = "",
}: PageShellProps) {
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const { user, logout } = useAuth();
  const [isMobileNavigationOpen, setIsMobileNavigationOpen] = useState(false);
  const [isSidebarFolded, setIsSidebarFolded] = useState(
    readSidebarFoldedPreference,
  );
  const mobileNavigationButtonRef = useRef<HTMLButtonElement | null>(null);
  const compactNavigationButtonRef = useRef<HTMLButtonElement | null>(null);
  const navigationTriggerRef = useRef<HTMLButtonElement | null>(null);

  const openMobileNavigation = useCallback(
    (trigger: HTMLButtonElement | null) => {
      navigationTriggerRef.current = trigger;
      setIsMobileNavigationOpen(true);
    },
    [],
  );

  const closeMobileNavigation = useCallback(() => {
    setIsMobileNavigationOpen(false);
    window.requestAnimationFrame(() => {
      navigationTriggerRef.current?.focus();
    });
  }, []);

  useEffect(() => {
    setIsMobileNavigationOpen(false);
    rememberActiveArea(pathname);
  }, [pathname]);

  useEffect(() => {
    window.localStorage.setItem(
      "cantaro:sidebar-folded",
      String(isSidebarFolded),
    );
  }, [isSidebarFolded]);

  const sidebarMode = isSidebarFolded ? "compact" : "expanded";

  return (
    <div className="min-h-screen bg-canvas text-content">
      <div
        className={getSidebarGridClassName(isSidebarFolded)}
        data-sidebar={sidebarMode}
      >
        <DesktopSidebar
          sidebar={sidebar}
          isFolded={isSidebarFolded}
          onToggle={() => setIsSidebarFolded((current) => !current)}
          compactButtonRef={compactNavigationButtonRef}
          onOpenCompact={() =>
            openMobileNavigation(compactNavigationButtonRef.current)
          }
        />

        <div className="flex min-w-0 flex-col pb-28">
          <PageTopBar
            pathname={pathname}
            displayName={user?.displayName}
            avatarUrl={user?.avatarUrl}
            onLogout={() => void logout()}
            navigationButtonRef={mobileNavigationButtonRef}
            onOpenNavigation={() =>
              openMobileNavigation(mobileNavigationButtonRef.current)
            }
          />

          <main
            className={`w-full px-3 py-5 sm:px-5 sm:py-6 lg:px-8 ${contentClassName}`}
          >
            {children}
          </main>
        </div>
      </div>

      <MobileNavigationDrawer
        isOpen={isMobileNavigationOpen}
        onClose={closeMobileNavigation}
      >
        {sidebar}
      </MobileNavigationDrawer>

      {bottomSlot}
    </div>
  );
}
