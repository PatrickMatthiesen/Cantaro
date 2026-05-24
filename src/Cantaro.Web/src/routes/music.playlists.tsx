import { createFileRoute } from '@tanstack/react-router';
import { MusicPlaylistsPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/playlists')({
  component: MusicPlaylistsPage,
});
