import { Link, useRouterState } from '@tanstack/react-router';
import { Clapperboard, Music2, Search } from 'lucide-react';
import { PageShell } from '../components/PageShell';
import { PageSideNavigation, type PageNavigationSection } from '../components/PageNavigation';
import { SearchGroupedResults } from './SearchResults';
import { searchGroups, searchTabs } from './searchGroups';
import { getSearchResultLimit, type SearchGroupId } from './searchState';
import { useSearchResults } from './useSearchResults';

interface SearchPageProps {
  query: string;
  activeGroup: SearchGroupId;
  preview?: boolean;
}

const searchNavigationSections: PageNavigationSection[] = [
  {
    title: 'Browse',
    items: [
      { label: 'Music library', to: '/music', icon: <Music2 className="h-4 w-4" />, tone: 'indigo' },
      { label: 'Media library', to: '/media/library', icon: <Clapperboard className="h-4 w-4" />, tone: 'rose' },
    ],
  },
];

function SearchSidebar() {
  const pathname = useRouterState({ select: (state) => state.location.pathname });

  return (
    <PageSideNavigation
      activePathname={pathname}
      subtitle="Search"
      sections={searchNavigationSections}
      footer={(
        <div className="bg-panel text-muted rounded-2xl p-4 text-sm leading-6">
          Search spans your personal archive and can discover media to add when you open the full results page.
        </div>
      )}
    />
  );
}

function SearchTabs({ activeGroup, query }: { activeGroup: SearchGroupId; query: string }) {
  return (
    <nav className="-mx-1 overflow-x-auto px-1 pb-1" aria-label="Search result groups">
      <div className="bg-soft flex min-w-max gap-1 rounded-2xl p-1">
        {searchTabs.map((tab) => {
          const active = tab.id === activeGroup;
          return (
            <Link
              key={tab.id}
              to="/search"
              search={{ q: query || undefined, group: tab.id }}
              className={`rounded-xl px-3.5 py-2 text-sm font-black transition focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none ${
                active ? 'bg-action text-action-content' : 'text-muted hover:bg-panel hover:text-ink'
              }`}
              aria-current={active ? 'page' : undefined}
            >
              {tab.label}
            </Link>
          );
        })}
      </div>
    </nav>
  );
}

function EmptyQueryState() {
  return (
    <div className="bg-panel rounded-2xl px-5 py-12 text-center sm:px-8 sm:py-16">
      <span className="bg-soft text-accent-content mx-auto flex h-12 w-12 items-center justify-center rounded-2xl">
        <Search className="h-5 w-5" aria-hidden />
      </span>
      <h2 className="text-ink mt-4 text-xl font-black">Search your personal archive</h2>
      <p className="text-muted mx-auto mt-2 max-w-lg text-sm leading-6 font-medium">
        Use the search field above to find songs, playlists, artists, and media across Cantaro.
      </p>
    </div>
  );
}

export function SearchPage({ query, activeGroup, preview = false }: SearchPageProps) {
  const normalizedQuery = query.trim();
  const results = useSearchResults(normalizedQuery, {
    enabled: !preview,
    includeDiscovery: true,
    limitPerGroup: getSearchResultLimit(activeGroup),
  });
  const groupIds = activeGroup === 'all'
    ? searchGroups.map((group) => group.id)
    : [activeGroup];

  return (
    <PageShell sidebar={<SearchSidebar />} contentClassName="search-page-content">
      <div className="mx-auto w-full max-w-5xl">
        <header className="mb-5">
          <h1 className="text-ink text-2xl font-black tracking-[-0.02em] sm:text-3xl">Search Cantaro</h1>
          <p className="text-muted mt-1 max-w-2xl text-sm leading-6 font-medium">
            {normalizedQuery ? `Results for “${normalizedQuery}”, including media you can add.` : 'Find something you saved or discover media to add.'}
          </p>
        </header>

        <SearchTabs activeGroup={activeGroup} query={normalizedQuery} />

        <div className="mt-5">
          {!normalizedQuery ? <EmptyQueryState /> : (
            <SearchGroupedResults
              query={normalizedQuery}
              response={results.response}
              error={results.error}
              loading={results.loading}
              groupIds={groupIds}
              showGroupLinks={activeGroup === 'all'}
              onRetry={results.retry}
              idPrefix="search-page"
            />
          )}
        </div>
      </div>
    </PageShell>
  );
}
