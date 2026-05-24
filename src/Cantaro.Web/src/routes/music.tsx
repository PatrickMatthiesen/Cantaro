import { createFileRoute } from '@tanstack/react-router';
import { MusicLayout } from '../pages/MusicPage';

export const Route = createFileRoute('/music')({
  component: MusicLayout,
});
