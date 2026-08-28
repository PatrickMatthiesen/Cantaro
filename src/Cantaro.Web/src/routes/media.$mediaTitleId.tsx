import { createFileRoute } from '@tanstack/react-router';
import { DETAIL_TABS, type DetailTabId } from '@cantaro/client-shared/media';
import { MediaTitleRoutePage } from '../pages/MediaPage';

export const Route = createFileRoute('/media/$mediaTitleId')({
  validateSearch: (search: Record<string, unknown>): { tab?: DetailTabId; franchise?: string } => ({
    tab: DETAIL_TABS.some((tab) => tab.id === search.tab) ? search.tab as DetailTabId : undefined,
    franchise: typeof search.franchise === 'string' && /^[0-9a-f-]{36}$/i.test(search.franchise)
      ? search.franchise
      : undefined,
  }),
  component: RouteComponent,
});

function RouteComponent() {
  const { mediaTitleId } = Route.useParams();
  const { tab, franchise } = Route.useSearch();
  return <MediaTitleRoutePage mediaTitleId={mediaTitleId} tab={tab} franchiseMediaTitleId={franchise} />;
}
