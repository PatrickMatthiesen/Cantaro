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
  return (
    <PageShell
      sidebar={<MediaSidebar />}
      searchPlaceholder="Search media, providers, reviews..."
    >
      {children}
    </PageShell>
  );
}
