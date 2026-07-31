import { Link } from '@tanstack/react-router';
import type { AppArea } from '../appAreaRouting';
import type { AppRouteTo } from '../routerTypes';

const navigationItems: Array<{ id: AppArea; label: string; to: AppRouteTo }> = [
  { id: 'music', label: 'Music', to: '/music' },
  { id: 'media', label: 'Media', to: '/media' },
];

function isItemActive(section: AppArea, pathname: string): boolean {
  return pathname === `/${section}` || pathname.startsWith(`/${section}/`);
}

export function AppNavigation({ pathname }: { pathname: string }) {
  return (
    <nav
      className="flex items-center gap-2"
      aria-label="Primary navigation"
    >
      {navigationItems.map((item) => {
        const isActive = isItemActive(item.id, pathname);

        return (
          <Link
            key={item.id}
            to={item.to}
            className={`inline-flex min-w-16 items-center justify-center rounded-2xl px-3 py-2.5 text-sm font-black transition sm:min-w-24 sm:px-5 sm:py-3 ${
              isActive
                ? 'app-nav-link--active'
                : 'text-content-muted hover:bg-surface/80'
            }`}
            aria-current={isActive ? 'page' : undefined}
          >
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}
