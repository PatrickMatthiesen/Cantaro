import {
  CalendarDays,
  Compass,
  Home,
  Library,
  PanelLeftClose,
  PanelLeftOpen,
  RefreshCw,
  X,
} from "lucide-react";
import type { RefObject } from "react";

const navItems = [
  { label: "Home", icon: Home },
  { label: "Library", icon: Library, active: true },
  { label: "Calendar", icon: CalendarDays },
  { label: "Discover", icon: Compass },
  { label: "Sync center", icon: RefreshCw },
];

export function Direction13Sidebar({
  compact,
  onToggle,
}: {
  compact: boolean;
  onToggle: () => void;
}) {
  return (
    <aside className="sticky top-0 hidden h-screen flex-col border-r border-border-subtle bg-canvas sm:flex">
      <a
        href="/"
        aria-label="Cantaro home"
        className="flex min-h-17 items-center gap-3 px-5 text-lg font-black"
      >
        <span className="inline-flex size-9 shrink-0 items-center justify-center bg-surface-subtle text-content ring-1 ring-border-strong">
          C
        </span>
        {!compact && <span className="hidden lg:inline">Cantaro</span>}
      </a>
      <nav aria-label="Primary" className="mt-6 space-y-1 px-3">
        {navItems.map(({ label, icon: Icon, active }) => (
          <a
            key={label}
            href="#quiet-overview"
            aria-current={active ? "page" : undefined}
            className={`flex min-h-11 items-center gap-3 px-3 text-sm font-semibold transition-colors ${active ? "bg-surface-subtle text-content" : "text-content-muted hover:bg-surface-hover hover:text-content"}`}
          >
            <Icon size={18} className="shrink-0" />
            {!compact && <span className="hidden lg:inline">{label}</span>}
          </a>
        ))}
      </nav>
      {!compact && (
        <div className="mt-8 hidden px-6 text-sm text-content-subtle lg:block">
          <p className="mb-3 text-xs font-semibold text-content-subtle">
            Your library
          </p>
          <div className="space-y-3">
            <p>Anime</p>
            <p>TV series</p>
            <p>Movies</p>
            <p>Watch later</p>
            <p>Favorites</p>
          </div>
        </div>
      )}
      <button
        type="button"
        onClick={onToggle}
        className="mt-auto hidden min-h-13 items-center gap-3 px-6 text-sm text-content-muted transition-colors hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-personal-accent lg:flex"
      >
        {compact ? <PanelLeftOpen size={18} /> : <PanelLeftClose size={18} />}
        {!compact && <span>Fold sidebar</span>}
      </button>
    </aside>
  );
}

export function Direction13MobileNavigation({
  dialogRef,
}: {
  dialogRef: RefObject<HTMLDialogElement | null>;
}) {
  const close = () => dialogRef.current?.close();
  return (
    <dialog
      ref={dialogRef}
      aria-label="Navigation menu"
      className="m-0 h-dvh max-h-none w-[min(88vw,20rem)] max-w-none bg-canvas p-0 text-content backdrop:bg-black/65 sm:hidden"
    >
      <div className="flex min-h-17 items-center justify-between border-b border-border-subtle px-5">
        <button
          autoFocus
          type="button"
          onClick={close}
          aria-label="Close navigation menu"
          className="order-2 inline-flex size-10 items-center justify-center text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-personal-accent"
        >
          <X size={20} />
        </button>
        <a
          href="/"
          onClick={close}
          className="order-1 flex items-center gap-3 font-black"
        >
          <span className="inline-flex size-9 items-center justify-center bg-surface-subtle ring-1 ring-border-strong">
            C
          </span>
          Cantaro
        </a>
      </div>
      <nav aria-label="Mobile primary" className="space-y-1 px-4 py-5">
        {navItems.map(({ label, icon: Icon, active }) => (
          <a
            key={label}
            href="#quiet-overview"
            onClick={close}
            aria-current={active ? "page" : undefined}
            className={`flex min-h-12 items-center gap-3 px-3 font-semibold ${active ? "bg-surface-subtle text-content" : "text-content-muted hover:bg-surface-hover hover:text-content"}`}
          >
            <Icon size={19} /> {label}
          </a>
        ))}
      </nav>
      <div className="border-t border-border-subtle px-7 py-5 text-sm text-content-muted">
        <p className="mb-3 text-xs font-semibold text-content-subtle">
          Your library
        </p>
        <div className="space-y-3">
          {["Anime", "TV series", "Movies", "Watch later", "Favorites"].map(
            (label) => (
              <a
                key={label}
                href="#quiet-overview"
                onClick={close}
                className="block hover:text-content"
              >
                {label}
              </a>
            ),
          )}
        </div>
      </div>
    </dialog>
  );
}
