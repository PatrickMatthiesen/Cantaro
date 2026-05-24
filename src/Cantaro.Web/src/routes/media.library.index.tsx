import { createFileRoute } from '@tanstack/react-router';
import { MediaLibraryRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/library/')({
  component: MediaLibraryRoutePage,
});
