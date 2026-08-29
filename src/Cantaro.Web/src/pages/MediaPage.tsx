import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import { Outlet, useNavigate, useRouterState } from '@tanstack/react-router';
import {
  MediaCatalogDetailPage,
  MediaEntryDetailPage,
  MediaLibraryPage,
  MediaObservationReviewPage,
  type DetailTabId,
} from '@cantaro/client-shared/media';
import { RequireAuth, type GlobalHeadingState } from '../components/AppShell';
import { MediaPageShell } from '../media/MediaPageShell';
import { MediaProvidersPage } from './MediaProvidersPage';
import { getMediaFilterDefaults } from '../media/mediaLibraryRouteFilters';
import { useAuth } from '../contexts/AuthContext';

const defaultMediaHeading: GlobalHeadingState = {
  eyebrow: 'Cantaro · Media',
  title: 'Media',
};

const MediaShellContext = createContext<{
  setHeading: (heading: GlobalHeadingState) => void;
} | null>(null);

function useMediaShell() {
  const context = useContext(MediaShellContext);
  if (!context) {
    throw new Error('useMediaShell must be used inside MediaLayout');
  }

  return context;
}

function useStaticMediaHeading(heading: GlobalHeadingState) {
  const { setHeading } = useMediaShell();

  useEffect(() => {
    setHeading(heading);
  }, [heading, setHeading]);
}

function MediaSectionHeader({ heading }: { heading: GlobalHeadingState }) {
  if (heading.hidden) {
    return null;
  }

  return (
    <header className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        <p className="text-sm font-semibold text-content-muted">{heading.eyebrow}</p>
        <h1 className="mt-1 text-3xl font-black text-content sm:text-4xl">{heading.title}</h1>
        {heading.details?.map((detail) => (
          <p key={detail} className="mt-1 text-sm text-content-muted">{detail}</p>
        ))}
      </div>
    </header>
  );
}

export function MediaLayout() {
  const [heading, setHeading] = useState(defaultMediaHeading);
  const contextValue = useMemo(() => ({ setHeading }), [setHeading]);

  return (
    <MediaPageShell>
      <MediaShellContext.Provider value={contextValue}>
        <MediaSectionHeader heading={heading} />
        <main className="space-y-5">
          <Outlet />
        </main>
      </MediaShellContext.Provider>
    </MediaPageShell>
  );
}

export function MediaLibraryRoutePage() {
  const navigate = useNavigate();
  const location = useRouterState({ select: (state) => state.location });
  const { setHeading } = useMediaShell();
  const search = location.search as Record<string, unknown>;
  const searchQuery = typeof search.q === 'string' ? search.q : '';
  const filterDefaults = useMemo(() => getMediaFilterDefaults(search), [search]);
  const updateSearchQuery = (query: string) => {
    void navigate({
      to: '/media/library',
      search: {
        ...search,
        q: query || undefined,
        searchMode: undefined,
      },
      replace: true,
    });
  };

  return (
    <MediaLibraryPage
      embedded
      searchQuery={searchQuery}
      filterDefaults={filterDefaults}
      onSearchQueryChange={updateSearchQuery}
      onHeadingChange={setHeading}
      onNavigateProviders={() => void navigate({ to: '/media/providers' })}
      onNavigateEntry={(id) => void navigate({ to: '/media/$mediaTitleId', params: { mediaTitleId: id } })}
    />
  );
}

export function MediaProvidersRoutePage() {
  const navigate = useNavigate();
  const heading = useMemo(() => ({
    eyebrow: 'Cantaro · Media',
    title: 'Media providers',
    hidden: true,
  }), []);
  useStaticMediaHeading(heading);

  return (
    <RequireAuth>
      <MediaProvidersPage
        embedded
        onNavigateLibrary={() => void navigate({ to: '/media/library' })}
      />
    </RequireAuth>
  );
}

export function MediaReviewRoutePage() {
  const heading = useMemo(() => ({
    eyebrow: 'Cantaro · Media',
    title: 'Resolve media matches',
  }), []);
  useStaticMediaHeading(heading);

  return (
    <RequireAuth>
      <MediaObservationReviewPage embedded />
    </RequireAuth>
  );
}

export function MediaTitleRoutePage({
  mediaTitleId,
  tab = 'overview',
  franchiseMediaTitleId,
}: {
  mediaTitleId: string;
  tab?: DetailTabId;
  franchiseMediaTitleId?: string;
}) {
  const navigate = useNavigate();
  const { user } = useAuth();
  const { setHeading } = useMediaShell();
  const heading = useMemo(() => ({
    eyebrow: 'Cantaro · Media',
    title: 'Media title',
    hidden: true,
  }), []);
  useStaticMediaHeading(heading);

  return (
    <MediaEntryDetailPage
      embedded
      mediaTitleId={mediaTitleId}
      preferredMediaReleaseTrack={user?.preferences.preferredMediaReleaseTrack}
      franchiseMediaTitleId={franchiseMediaTitleId}
      activeTab={tab}
      onTabChange={(nextTab) => void navigate({
        to: '/media/$mediaTitleId',
        params: { mediaTitleId },
        resetScroll: false,
        search: {
          ...(nextTab === 'overview' ? {} : { tab: nextTab }),
          ...(franchiseMediaTitleId ? { franchise: franchiseMediaTitleId } : {}),
        },
      })}
      onHeadingChange={setHeading}
      onNavigateBack={() => void navigate({ to: '/media/library' })}
      onNavigateTitle={(relatedMediaTitleId) => void navigate({
        to: '/media/$mediaTitleId',
        params: { mediaTitleId: relatedMediaTitleId },
        search: tab === 'franchise'
          ? { franchise: franchiseMediaTitleId ?? mediaTitleId }
          : {},
      })}
    />
  );
}

export function MediaCatalogRoutePage({ providerId, providerMediaId }: { providerId: string; providerMediaId: string }) {
  const navigate = useNavigate();
  const heading = useMemo(() => ({
    eyebrow: 'Cantaro · Media',
    title: 'Catalog result',
  }), []);
  useStaticMediaHeading(heading);

  return (
    <RequireAuth>
      <MediaCatalogDetailPage
        embedded
        providerId={providerId}
        providerMediaId={providerMediaId}
        onNavigateBack={() => void navigate({ to: '/media/library' })}
        onNavigateTitle={(mediaTitleId) => void navigate({
          to: '/media/$mediaTitleId',
          params: { mediaTitleId },
          replace: true,
        })}
      />
    </RequireAuth>
  );
}
