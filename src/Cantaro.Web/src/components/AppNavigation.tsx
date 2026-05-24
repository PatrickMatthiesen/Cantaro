import { Link } from '@tanstack/react-router';
import type { AppRouteTo } from '../routerTypes';

type AppSection = 'home' | 'music' | 'media';

const navigationItems: Array<{ id: AppSection; label: string; to: AppRouteTo }> = [
  { id: 'home', label: 'Home', to: '/' },
  { id: 'music', label: 'Music', to: '/music/songs' },
  { id: 'media', label: 'Media', to: '/media' },
];

function isItemActive(section: AppSection, pathname: string): boolean {
  if (section === 'home') return pathname === '/';
  return pathname === `/${section}` || pathname.startsWith(`/${section}/`);
}

export function AppNavigation({ pathname }: { pathname: string }) {
  return (
    <nav
      className="flex flex-wrap items-center gap-2 rounded-2xl border border-white/80 bg-white/55 p-1.5 shadow-[0_8px_24px_rgba(31,41,55,0.06)] backdrop-blur"
      aria-label="Primary navigation"
    >
      {navigationItems.map((item) => {
        const isActive = isItemActive(item.id, pathname);

        return (
          <Link
            key={item.id}
            to={item.to}
            className={`inline-flex min-w-24 items-center justify-center rounded-2xl px-4 py-2 text-sm font-semibold transition-all hover:scale-[1.03] ${
              isActive
                ? 'bg-gray-900 text-white hover:bg-gray-700'
                : 'bg-white/70 text-gray-800 hover:bg-mist-100'
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
