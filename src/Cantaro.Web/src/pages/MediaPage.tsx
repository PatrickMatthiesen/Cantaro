import { Outlet, useNavigate } from '@tanstack/react-router';
import { MediaCatalogDetailPage, MediaEntryDetailPage, MediaLibraryPage } from '@cantaro/client-shared/media';
import { AppPageShell, GlobalHeader, RequireAuth } from '../components/AppShell';
import { SubjectNav, type SubjectNavItem } from '../components/SubjectNav';
import { MediaProvidersPage } from './MediaProvidersPage';

const mediaNavItems: SubjectNavItem[] = [
  { label: 'Library', to: '/media/library' },
  { label: 'Providers', to: '/media/providers' },
];

export function MediaLayout() {
  return (
    <RequireAuth>
      <AppPageShell contentClassName="max-w-7xl">
        <GlobalHeader eyebrow="Cantaro · Media" title="Media tracking" />
        <main className="space-y-5">
          <SubjectNav label="Media sections" items={mediaNavItems} />
          <Outlet />
        </main>
      </AppPageShell>
    </RequireAuth>
  );
}

export function MediaLibraryRoutePage() {
  const navigate = useNavigate();

  return (
    <MediaLibraryPage
      embedded
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

  return (
    <MediaProvidersPage
      embedded
      onNavigateLibrary={() => void navigate({ to: '/media/library' })}
    />
  );
}

export function MediaEntryRoutePage({ entryId }: { entryId: string }) {
  const navigate = useNavigate();

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
