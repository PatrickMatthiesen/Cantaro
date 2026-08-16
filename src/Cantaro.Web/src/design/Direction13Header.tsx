import { Bell, Menu, Search } from "lucide-react";

export function Direction13Switcher() {
  return (
    <nav
      aria-label="Design directions"
      className="flex flex-wrap items-center gap-1"
    >
      <span className="mr-3 text-xs font-medium text-content-muted">
        Direction
      </span>
      {Array.from({ length: 13 }, (_, index) => index + 1).map((number) => (
        <a
          key={number}
          href={`/${number}`}
          aria-current={number === 13 ? "page" : undefined}
          className={`inline-flex size-9 items-center justify-center text-sm font-bold outline-none transition-colors focus-visible:ring-2 focus-visible:ring-personal-accent ${number === 13 ? "text-personal-accent-strong ring-1 ring-personal-accent" : "text-content-muted hover:bg-surface-hover hover:text-content"}`}
        >
          {number}
        </a>
      ))}
    </nav>
  );
}

function AccountMenu() {
  return (
    <details className="group relative">
      <summary
        aria-label="Open account menu for Kael Ardent"
        className="flex size-10 cursor-pointer list-none items-center justify-center rounded-full bg-surface-subtle text-sm font-black text-content outline-none transition-colors hover:bg-surface-hover hover:text-personal-accent-strong focus-visible:ring-2 focus-visible:ring-personal-accent [&::-webkit-details-marker]:hidden"
      >
        K
      </summary>
      <div className="absolute right-0 z-40 mt-3 w-64 bg-surface-raised p-4 text-sm shadow-lg shadow-black/30">
        <p className="font-bold">Kael Ardent</p>
        <p className="mt-1 text-xs text-content-muted">
          Personal archive controls
        </p>
        <a
          href="/settings"
          className="mt-4 block py-2 text-content hover:text-personal-accent-strong"
        >
          Settings
        </a>
        <button
          type="button"
          className="block w-full py-2 text-left text-content hover:text-danger-content"
        >
          Log out
        </button>
      </div>
    </details>
  );
}

export function Direction13Header({
  onOpenNavigation,
}: {
  onOpenNavigation: () => void;
}) {
  return (
    <header className="sticky top-0 z-30 border-b border-border-subtle bg-canvas/95 px-4 backdrop-blur-md sm:px-7 xl:px-9">
      <div className="flex h-17 items-center gap-3">
        <button
          type="button"
          onClick={onOpenNavigation}
          aria-label="Open navigation menu"
          className="inline-flex size-10 items-center justify-center text-content-muted hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-personal-accent sm:hidden"
        >
          <Menu size={20} />
        </button>
        <label className="hidden min-w-0 max-w-2xl flex-1 items-center gap-3 border border-border-subtle bg-transparent px-4 text-content-subtle md:flex focus-within:border-border-strong">
          <Search size={18} />
          <span className="sr-only">Search Cantaro</span>
          <input
            aria-label="Search Cantaro"
            placeholder="Search anime, series, movies..."
            className="h-11 min-w-0 flex-1 bg-transparent text-sm text-content outline-none placeholder:text-content-subtle"
          />
        </label>
        <div className="ml-auto flex items-center gap-2">
          <a
            href="/search?group=all&preview=true"
            aria-label="Search Cantaro"
            className="inline-flex size-10 items-center justify-center text-content-muted hover:bg-surface-hover hover:text-content md:hidden"
          >
            <Search size={19} />
          </a>
          <button
            type="button"
            aria-label="Notifications"
            className="inline-flex size-10 items-center justify-center text-content-muted hover:bg-surface-raised hover:text-personal-accent-strong"
          >
            <Bell size={18} />
          </button>
          <AccountMenu />
        </div>
      </div>
    </header>
  );
}
