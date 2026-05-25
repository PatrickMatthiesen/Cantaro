import { createFileRoute } from '@tanstack/react-router';
import { MediaCatalogRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/catalog/$providerId/$providerMediaId')({
  component: RouteComponent,
});

function RouteComponent() {
  const { providerId, providerMediaId } = Route.useParams();
  return <MediaCatalogRoutePage providerId={providerId} providerMediaId={providerMediaId} />;
}
