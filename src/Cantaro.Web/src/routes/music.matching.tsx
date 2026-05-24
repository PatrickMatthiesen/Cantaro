import { createFileRoute } from '@tanstack/react-router';
import { MusicMatchingPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/matching')({
  component: MusicMatchingPage,
});
