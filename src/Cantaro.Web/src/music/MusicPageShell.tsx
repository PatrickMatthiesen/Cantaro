import {
  MusicPlatformIcon,
  MusicUiIcon,
  platformCatalog,
  type MusicLibraryResponse,
  type MusicLibrarySong,
} from '@cantaro/client-shared/music';
import { Link, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { PageSideNavigation, type PageNavigationSection } from '../components/PageNavigation';
import { PageShell } from '../components/PageShell';
import { useMusicLibraryContext } from './MusicLibraryStateContext';
import { playlistArtwork } from './musicPresentation';

function createMusicNavigationSections(): PageNavigationSection[] {
  return [
    {
      title: 'Music',
      items: [
        { label: 'Songs', to: '/music/songs', icon: <MusicUiIcon name="music" className="h-4 w-4" />, matchPrefix: '/music/songs', tone: 'indigo' },
        { label: 'Playlists', to: '/music/playlists', icon: <MusicUiIcon name="listMusic" className="h-4 w-4" />, matchPrefix: '/music/playlists', tone: 'violet' },
        { label: 'Matching', to: '/music/matching', icon: <MusicUiIcon name="sparkles" className="h-4 w-4" />, matchPrefix: '/music/matching', tone: 'pink' },
        { label: 'Playlist sync', to: '/music/platforms', icon: <MusicUiIcon name="refresh" className="h-4 w-4" />, exact: true, tone: 'emerald' },
      ],
    },
    {
      title: 'Platforms',
      titleAction: {
        label: 'Manage',
        to: '/music/platforms',
        icon: <MusicUiIcon name="settings" className="h-3.5 w-3.5" />,
        ariaLabel: 'Manage platforms',
      },
      items: platformCatalog.map((platform) => ({
        label: platform.name,
        to: '/music/platforms/$platformId',
        params: { platformId: platform.id },
        icon: <MusicPlatformIcon platformId={platform.iconId} className="h-4 w-4" />,
        tone: platform.id === 'youtube' ? 'red' : platform.id === 'spotify' ? 'emerald' : platform.id === 'apple' ? 'pink' : 'violet',
        detail: platform.implemented ? 'Available' : 'Coming soon',
        disabled: !platform.implemented,
        matchPrefix: `/music/platforms/${platform.id}`,
      })),
    },
  ];
}

function RecentPlaylists({ library }: { library?: MusicLibraryResponse }) {
  const playlists = library?.playlists.slice(0, 5) ?? [];
  if (playlists.length === 0) return null;

  return (
    <section className="border-t border-border-subtle pt-5">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-sm font-bold text-content">Recent playlists</h2>
        <Link to="/music/playlists" className="text-xs font-semibold text-content-muted hover:text-personal-accent-strong">View all</Link>
      </div>
      <div className="mt-3 space-y-1">
        {playlists.map((playlist, index) => (
          <Link
            key={playlist.id}
            to="/music/playlists/$playlistId"
            params={{ playlistId: playlist.id }}
            className="group flex min-h-11 items-center gap-3 px-1 text-sm font-semibold text-content transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus"
          >
            <img src={playlistArtwork(playlist, index)} alt="" className="size-8 object-cover" />
            <span className="min-w-0 flex-1 truncate group-data-[sidebar=compact]/sidebar:hidden">{playlist.name}</span>
          </Link>
        ))}
      </div>
    </section>
  );
}

function MusicSidebar({ library }: { library?: MusicLibraryResponse }) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  return (
    <PageSideNavigation
      activePathname={pathname}
      subtitle="Music"
      sections={createMusicNavigationSections()}
      footer={<RecentPlaylists library={library} />}
    />
  );
}

export function MusicPageShell(props: {
  children: ReactNode;
  library?: MusicLibraryResponse;
  activeSong?: MusicLibrarySong;
  onStopActiveSong?: () => void;
}) {
  const { library: sharedLibrary } = useMusicLibraryContext();
  const sidebarLibrary = props.library ?? sharedLibrary ?? undefined;

  return (
    <PageShell sidebar={<MusicSidebar library={sidebarLibrary} />}>
      {props.children}
    </PageShell>
  );
}
