import { mainMediaProviderId, mediaApi } from '@cantaro/client-shared/media';
import { createFileRoute, Outlet, redirect } from '@tanstack/react-router';
import { RequireAuth } from '../components/AppShell';
import { shouldRedirectEmptyLibraryToProviders } from '../media/mediaLibraryRouting';

export const Route = createFileRoute('/media/library')({
  beforeLoad: async () => {
    const [libraryResult, providerStatusResult] = await Promise.allSettled([
      mediaApi.getLibrary({ page: 1, pageSize: 1 }),
      mediaApi.getProviderStatus(mainMediaProviderId),
    ]);

    if (providerStatusResult.status === 'fulfilled'
      && shouldRedirectEmptyLibraryToProviders(
        libraryResult.status === 'fulfilled' ? libraryResult.value : null,
        providerStatusResult.value,
      )) {
      throw redirect({ to: '/media/providers', replace: true });
    }
  },
  component: () => (
    <RequireAuth>
      <Outlet />
    </RequireAuth>
  ),
});
