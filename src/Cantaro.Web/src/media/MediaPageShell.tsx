import { useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
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
        {
          label: 'Anime',
          to: '/media/library',
          icon: 'A',
          search: { mediaKind: 'anime', sortBy: 'updatedAt', sortDir: 'desc' },
          matchPrefix: '/media/browse/anime',
        },
        {
          label: 'Manga',
          to: '/media/library',
          icon: 'M',
          search: { mediaKind: 'manga', sortBy: 'updatedAt', sortDir: 'desc' },
          matchPrefix: '/media/browse/manga',
        },
        {
          label: 'Recently Updated',
          to: '/media/library',
          icon: 'U',
          search: { sortBy: 'updatedAt', sortDir: 'desc' },
          matchPrefix: '/media/browse/recently-updated',
        },
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
        <div className="rounded-3xl bg-surface-translucent p-5 shadow-[0_20px_60px_rgba(88,74,150,0.08)]">
          <p className="text-lg font-black">Review queue</p>
          <p className="mt-2 text-sm leading-6 text-content-muted">Resolve new media observations and keep the library tidy.</p>
        </div>
      }
    />
  );
}

export function MediaPageShell({ children }: { children: ReactNode }) {
  return (
    <PageShell
      sidebar={<MediaSidebar />}
      contentClassName="media-page-content"
    >
      {children}
    </PageShell>
  );
}
