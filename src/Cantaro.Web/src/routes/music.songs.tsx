import { createFileRoute } from '@tanstack/react-router';
import { MusicSongsPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/songs')({
  component: MusicSongsPage,
});
