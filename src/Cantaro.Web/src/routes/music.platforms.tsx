import { createFileRoute } from '@tanstack/react-router';
import { MusicPlatformsPage } from '../music/MusicPlatformsPage';

export const Route = createFileRoute('/music/platforms')({
  component: MusicPlatformsPage,
});
