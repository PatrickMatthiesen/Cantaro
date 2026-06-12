import { useNavigate, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { mainMediaProviderId } from '@cantaro/client-shared/media';
import { PageShell } from '../components/PageShell';
import { PageSideNavigation, type PageNavigationSection } from '../components/PageNavigation';

function createMediaNavigationSections(): PageNavigationSection[] {
  return [
    {
      title: 'Media',
      items: [
        { label: 'Library', to: '/media/library', icon: 'L', matchPrefix: '/media/library' },
        { label: 'Review', to: '/media/review', icon: 'R', matchPrefix: '/media/review' },
        { label: 'Providers', to: '/media/providers', icon: 'P', matchPrefix: '/media/providers' },
      ],
    },
    {
      title: 'Browse',
      items: [
        { label: 'Anime', to: '/media/library', icon: 'A', matchPrefix: '/media/browse/anime' },
        { label: 'Manga', to: '/media/library', icon: 'M', matchPrefix: '/media/browse/manga' },
        { label: 'Recently Updated', to: '/media/library', icon: 'U', matchPrefix: '/media/browse/recently-updated' },
      ],
    },
  ];
}

function MediaSidebar() {
  const pathname = useRouterState({ select: (state) => state.location.pathname });

  return (
    <PageSideNavigation
      activePathname={pathname}
      subtitle="Media"
      sections={createMediaNavigationSections()}
      footer={
        <div className="rounded-3xl bg-white/70 p-5 shadow-[0_20px_60px_rgba(88,74,150,0.08)]">
          <p className="text-lg font-black">Review queue</p>
          <p className="mt-2 text-sm leading-6 text-slate-600">Resolve new media observations and keep the library tidy.</p>
        </div>
      }
    />
  );
}

export function MediaPageShell({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const location = useRouterState({ select: (state) => state.location });
  const search = location.search as Record<string, unknown>;
  const searchValue = typeof search.q === 'string' ? search.q : '';

  const updateLibrarySearch = (value: string) => {
    void navigate({
      to: '/media/library',
      search: {
        ...search,
        q: value || undefined,
        searchMode: typeof search.searchMode === 'string' ? search.searchMode : 'library',
      },
      replace: location.pathname === '/media/library',
    });
  };

  return (
    <PageShell
      sidebar={<MediaSidebar />}
      searchPlaceholder={search.searchMode === mainMediaProviderId ? 'Search AniList...' : 'Search media library...'}
      searchValue={searchValue}
      onSearchChange={updateLibrarySearch}
      onSearchSubmit={() => updateLibrarySearch(searchValue)}
    >
      {children}
    </PageShell>
  );
}
