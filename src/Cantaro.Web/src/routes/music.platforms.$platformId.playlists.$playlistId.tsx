import { createFileRoute } from '@tanstack/react-router';
import { MusicPlatformPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/platforms/$platformId/playlists/$playlistId')({
  component: RouteComponent,
});

function RouteComponent() {
  const { platformId, playlistId } = Route.useParams();
  return <MusicPlatformPage platformId={platformId} playlistId={playlistId} />;
}
