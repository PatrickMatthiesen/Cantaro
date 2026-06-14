import { createFileRoute } from '@tanstack/react-router';
import { MusicRootPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/')({
  component: MusicRootPage,
});
