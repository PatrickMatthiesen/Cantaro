import { createFileRoute } from '@tanstack/react-router';
import { MediaReviewRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/review')({
  component: MediaReviewRoutePage,
});
