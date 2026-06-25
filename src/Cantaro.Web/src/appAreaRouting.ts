import type { AppRouteTo } from './routerTypes';

export type AppArea = 'music' | 'media';

const lastActiveAreaStorageKey = 'cantaro:last-active-area';

const areaRoutes: Record<AppArea, AppRouteTo> = {
  music: '/music',
  media: '/media',
};

function getAreaFromPathname(pathname: string): AppArea | null {
  if (pathname === '/music' || pathname.startsWith('/music/')) return 'music';
  if (pathname === '/media' || pathname.startsWith('/media/')) return 'media';
  return null;
}

function isAppArea(value: string | null): value is AppArea {
  return value === 'music' || value === 'media';
}

export function rememberActiveArea(pathname: string) {
  const area = getAreaFromPathname(pathname);
  if (!area) return;

  try {
    window.localStorage.setItem(lastActiveAreaStorageKey, area);
  } catch {
    // Storage can be unavailable in private browsing or hardened browser contexts.
  }
}

export function getLastActiveAreaRoute(): AppRouteTo {
  try {
    const area = window.localStorage.getItem(lastActiveAreaStorageKey);
    return isAppArea(area) ? areaRoutes[area] : areaRoutes.music;
  } catch {
    return areaRoutes.music;
  }
}
