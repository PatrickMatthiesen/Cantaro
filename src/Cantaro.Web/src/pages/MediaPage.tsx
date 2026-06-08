import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import { Outlet, useNavigate } from '@tanstack/react-router';
import {
  MediaCatalogDetailPage,
  MediaEntryDetailPage,
  MediaLibraryPage,
  MediaObservationReviewPage,
} from '@cantaro/client-shared/media';
import { AppPageShell, GlobalHeader, RequireAuth, type GlobalHeadingState } from '../components/AppShell';
import { SubjectNav, type SubjectNavItem } from '../components/SubjectNav';
import { MediaProvidersPage } from './MediaProvidersPage';

const mediaNavItems: SubjectNavItem[] = [
  { label: 'Library', to: '/media/library' },
  { label: 'Review', to: '/media/review' },
  { label: 'Providers', to: '/media/providers' },
];

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

function MediaSectionNav({ integrated = false }: { integrated?: boolean }) {
  return <SubjectNav label="Media sections" items={mediaNavItems} chrome={integrated ? 'bare' : 'panel'} />;
}

function useStaticMediaHeading(heading: GlobalHeadingState) {
  const { setHeading } = useMediaShell();

  useEffect(() => {
    setHeading(heading);
  }, [heading, setHeading]);
}

export function MediaLayout() {
  const [heading, setHeading] = useState(defaultMediaHeading);
  const contextValue = useMemo(() => ({ setHeading }), [setHeading]);

  return (
    <RequireAuth>
      <AppPageShell contentClassName="max-w-7xl">
        <MediaShellContext.Provider value={contextValue}>
          <GlobalHeader heading={heading} />
          <main className="space-y-5">
            <Outlet />
          </main>
        </MediaShellContext.Provider>
      </AppPageShell>
    </RequireAuth>
  );
}

export function MediaLibraryRoutePage() {
  const navigate = useNavigate();
  const { setHeading } = useMediaShell();

  return (
    <MediaLibraryPage
      embedded
      navigation={<MediaSectionNav integrated />}
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
      navigation={<MediaSectionNav />}
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
      navigation={<MediaSectionNav />}
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
