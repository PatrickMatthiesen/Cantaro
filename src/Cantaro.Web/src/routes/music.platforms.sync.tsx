import { createFileRoute } from '@tanstack/react-router';
import { platformCatalog, type PlatformId } from '@cantaro/client-shared/music';
import { MusicPlatformSyncSetupPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/platforms/sync')({
  validateSearch: (search: Record<string, unknown>): { source?: PlatformId } => {
    const source = platformCatalog.find((candidate) => candidate.id === search.source)?.id;
    return source ? { source } : {};
  },
  component: RouteComponent,
});

function RouteComponent() {
  const { source } = Route.useSearch();
  return <MusicPlatformSyncSetupPage initialSourcePlatformId={source} />;
}
