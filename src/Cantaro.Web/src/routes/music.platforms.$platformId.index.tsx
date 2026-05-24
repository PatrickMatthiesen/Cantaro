import { createFileRoute } from '@tanstack/react-router';
import { MusicPlatformPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/platforms/$platformId/')({
  component: RouteComponent,
});

function RouteComponent() {
  const { platformId } = Route.useParams();
  return <MusicPlatformPage platformId={platformId} />;
}
