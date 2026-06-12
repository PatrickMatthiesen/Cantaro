import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import { Outlet, useNavigate, useRouterState } from '@tanstack/react-router';
import {
  MediaCatalogDetailPage,
  MediaEntryDetailPage,
  MediaLibraryPage,
  MediaObservationReviewPage,
} from '@cantaro/client-shared/media';
import { RequireAuth, type GlobalHeadingState } from '../components/AppShell';
import { MediaPageShell } from '../media/MediaPageShell';
import { MediaProvidersPage } from './MediaProvidersPage';

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
  return (
    <header className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        <p className="text-xs font-black tracking-[0.22em] text-violet-600 uppercase">{heading.eyebrow}</p>
        <h1 className="mt-2 text-4xl font-black text-slate-950">{heading.title}</h1>
        {heading.details?.map((detail) => (
          <p key={detail} className="mt-1 text-sm font-semibold text-slate-500">{detail}</p>
        ))}
      </div>
    </header>
  );
}

export function MediaLayout() {
  const [heading, setHeading] = useState(defaultMediaHeading);
  const contextValue = useMemo(() => ({ setHeading }), [setHeading]);

  return (
    <RequireAuth>
      <MediaPageShell>
        <MediaShellContext.Provider value={contextValue}>
          <MediaSectionHeader heading={heading} />
          <main className="space-y-5">
            <Outlet />
          </main>
        </MediaShellContext.Provider>
      </MediaPageShell>
    </RequireAuth>
  );
}

export function MediaLibraryRoutePage() {
  const navigate = useNavigate();
  const location = useRouterState({ select: (state) => state.location });
  const { setHeading } = useMediaShell();
  const search = location.search as Record<string, unknown>;
  const searchQuery = typeof search.q === 'string' ? search.q : '';
  const searchMode = typeof search.searchMode === 'string' ? search.searchMode : 'library';
  const updateSearchState = (next: { query?: string; mode?: string }) => {
    void navigate({
      to: '/media/library',
      search: {
        ...search,
        q: next.query || undefined,
        searchMode: next.mode ?? searchMode,
      },
      replace: true,
    });
  };

  return (
    <MediaLibraryPage
      embedded
      searchQuery={searchQuery}
      searchMode={searchMode}
      onSearchQueryChange={(query) => updateSearchState({ query })}
      onSearchModeChange={(mode) => updateSearchState({ query: searchQuery, mode })}
      onHeadingChange={setHeading}
      onNavigateProviders={() => void navigate({ to: '/media/providers' })}
      onNavigateEntry={(id) => void navigate({ to: '/media/library/$entryId', params: { entryId: id } })}
      onNavigateCatalogResult={(providerId, providerMediaId) => void navigate({
        to: '/media/catalog/$providerId/$providerMediaId',
        params: { providerId, providerMediaId },
      })}
    />
  );
}

export function MediaProvidersRoutePage() {
  const navigate = useNavigate();
  const heading = useMemo(() => ({
    eyebrow: 'Cantaro · Media',
    title: 'Media providers',
  }), []);
  useStaticMediaHeading(heading);

  return (
    <MediaProvidersPage
      embedded
      onNavigateLibrary={() => void navigate({ to: '/media/library' })}
    />
  );
}

export function MediaReviewRoutePage() {
  const heading = useMemo(() => ({
    eyebrow: 'Cantaro · Media',
    title: 'Resolve media matches',
  }), []);
  useStaticMediaHeading(heading);

  return (
    <MediaObservationReviewPage
      embedded
    />
  );
}

export function MediaEntryRoutePage({ entryId }: { entryId: string }) {
  const navigate = useNavigate();
  const heading = useMemo(() => ({
    eyebrow: 'Cantaro · Media',
    title: 'Library entry',
  }), []);
  useStaticMediaHeading(heading);

  return (
    <MediaEntryDetailPage
      embedded
      libraryEntryId={entryId}
      onNavigateBack={() => void navigate({ to: '/media/library' })}
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
    <MediaCatalogDetailPage
      embedded
      providerId={providerId}
      providerMediaId={providerMediaId}
      onNavigateBack={() => void navigate({ to: '/media/library' })}
      onNavigateEntry={(entryId) => void navigate({ to: '/media/library/$entryId', params: { entryId } })}
    />
  );
}
