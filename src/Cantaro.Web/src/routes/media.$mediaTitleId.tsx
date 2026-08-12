import { createFileRoute } from '@tanstack/react-router';
import { MediaTitleRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/$mediaTitleId')({
  component: RouteComponent,
});

function RouteComponent() {
  const { mediaTitleId } = Route.useParams();
  return <MediaTitleRoutePage mediaTitleId={mediaTitleId} />;
}
