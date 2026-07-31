import { createFileRoute } from '@tanstack/react-router';
import { RequireAuth } from '../components/AppShell';
import { SearchPage } from '../search/SearchPage';
import { readSearchRouteState } from '../search/searchState';
import { useMobileSearchViewport } from '../search/useSearchViewport';

export const Route = createFileRoute('/search')({
  validateSearch: readSearchRouteState,
  component: SearchRoute,
});

function SearchRoute() {
  const search = Route.useSearch();
  const isMobile = useMobileSearchViewport();
  return (
    <RequireAuth>
      <SearchPage
        query={search.q ?? ''}
        activeGroup={search.group}
        preview={search.preview === true && isMobile}
      />
    </RequireAuth>
  );
}
