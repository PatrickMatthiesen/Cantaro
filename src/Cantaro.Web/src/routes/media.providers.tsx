import { createFileRoute } from '@tanstack/react-router';
import { MediaProvidersRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/providers')({
  component: MediaProvidersRoutePage,
});
