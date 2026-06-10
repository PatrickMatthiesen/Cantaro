import { createFileRoute } from '@tanstack/react-router';
import { MusicPlaylistPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/playlists/$playlistId')({
  component: RouteComponent,
});

function RouteComponent() {
  const { playlistId } = Route.useParams();
  return <MusicPlaylistPage playlistId={playlistId} />;
}
