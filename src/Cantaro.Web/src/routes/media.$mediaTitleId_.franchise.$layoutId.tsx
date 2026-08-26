import { createFileRoute } from '@tanstack/react-router';
import type { FranchiseDesignVariant } from '@cantaro/client-shared/media';
import { MediaFranchiseDesignRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/$mediaTitleId_/franchise/$layoutId')({
  component: RouteComponent,
});

function parseVariant(value: string): FranchiseDesignVariant {
  const parsed = Number(value);
  return parsed >= 1 && parsed <= 8 ? parsed as FranchiseDesignVariant : 3;
}

function RouteComponent() {
  const { mediaTitleId, layoutId } = Route.useParams();
  const variant = parseVariant(layoutId);

  return <MediaFranchiseDesignRoutePage mediaTitleId={mediaTitleId} variant={variant} />;
}
