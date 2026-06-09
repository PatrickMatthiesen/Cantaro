import { Link } from '@tanstack/react-router';
import type { AppRouteTo } from '../routerTypes';

type AppSection = 'music' | 'media';

const navigationItems: Array<{ id: AppSection; label: string; to: AppRouteTo }> = [
  { id: 'music', label: 'Music', to: '/music/songs' },
  { id: 'media', label: 'Media', to: '/media' },
];

function isItemActive(section: AppSection, pathname: string): boolean {
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
            className={`inline-flex min-w-24 items-center justify-center rounded-2xl px-5 py-3 text-sm font-black transition ${
              isActive
                ? 'bg-[#ebe7ff] text-slate-950'
                : 'text-slate-700 hover:bg-white/80'
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
