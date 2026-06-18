import { createFileRoute } from '@tanstack/react-router';
import { MusicSongPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/songs/$songId')({
  component: SongDetailRoute,
});

function SongDetailRoute() {
  const { songId } = Route.useParams();
  return <MusicSongPage songId={songId} />;
}
