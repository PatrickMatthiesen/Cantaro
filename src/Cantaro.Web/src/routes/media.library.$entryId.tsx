import { createFileRoute } from '@tanstack/react-router';
import { MediaEntryRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/library/$entryId')({
  component: RouteComponent,
});

function RouteComponent() {
  const { entryId } = Route.useParams();
  return <MediaEntryRoutePage entryId={entryId} />;
}
