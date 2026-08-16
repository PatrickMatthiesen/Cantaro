import { Link } from "@tanstack/react-router";
import type { AppArea } from "../appAreaRouting";
import type { AppRouteTo } from "../routerTypes";

const navigationItems: Array<{ id: AppArea; label: string; to: AppRouteTo }> = [
  { id: "music", label: "Music", to: "/music" },
  { id: "media", label: "Media", to: "/media" },
];

function isItemActive(section: AppArea, pathname: string): boolean {
  return pathname === `/${section}` || pathname.startsWith(`/${section}/`);
}

export function AppNavigation({ pathname }: { pathname: string }) {
  return (
    <nav className="flex items-center" aria-label="Primary navigation">
      {navigationItems.map((item) => {
        const isActive = isItemActive(item.id, pathname);

        return (
          <Link
            key={item.id}
            to={item.to}
            className={`inline-flex min-h-11 items-center justify-center border-b-2 px-2.5 text-sm font-bold transition-colors sm:px-4 ${
              isActive
                ? "border-personal-accent text-content"
                : "border-transparent text-content-muted hover:bg-surface-hover hover:text-content"
            }`}
            aria-current={isActive ? "page" : undefined}
          >
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}
