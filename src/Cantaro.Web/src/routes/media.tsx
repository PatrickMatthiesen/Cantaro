import { createFileRoute } from '@tanstack/react-router';
import { MediaLayout } from '../pages/MediaPage';

export const Route = createFileRoute('/media')({
  component: MediaLayout,
});
