import { Link, useRouterState } from '@tanstack/react-router';
import {
  AlertTriangle,
  ArrowRight,
  Clapperboard,
  Library,
  ListMusic,
  Music2,
  RotateCcw,
  Search,
  UserRound,
  type LucideIcon,
} from 'lucide-react';
import { useEffect, useState } from 'react';
import { PageShell } from '../components/PageShell';
import { PageSideNavigation, type PageNavigationSection } from '../components/PageNavigation';
import {
  searchCantaro,
  type SearchEntityType,
  type SearchResponse,
  type SearchResultGroup,
  type SearchResultGroupId,
  type SearchResultItem,
} from './searchApi';
import { getSearchResultLimit, type SearchGroupId } from './searchState';

interface SearchPageProps {
  query: string;
  activeGroup: SearchGroupId;
}

interface SearchGroupPresentation {
  id: SearchResultGroupId;
  label: string;
  singular: string;
  description: string;
  icon: LucideIcon;
}

const searchGroups: SearchGroupPresentation[] = [
  { id: 'songs', label: 'Songs', singular: 'song', description: 'Recordings in your music archive', icon: Music2 },
  { id: 'artists', label: 'Artists', singular: 'artist', description: 'Artists connected to your saved music', icon: UserRound },
  { id: 'playlists', label: 'Playlists', singular: 'playlist', description: 'Collections owned by you', icon: ListMusic },
  { id: 'media', label: 'Media', singular: 'title', description: 'Movies, series, anime, and manga in your library', icon: Clapperboard },
];

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
          Search only returns music and media that belong to your personal Cantaro archive.
        </div>
      )}
    />
  );
}

function EntityIcon({ entityType }: { entityType: SearchEntityType }) {
  switch (entityType) {
    case 'song':
      return <Music2 className="h-5 w-5" aria-hidden />;
    case 'artist':
      return <UserRound className="h-5 w-5" aria-hidden />;
    case 'playlist':
      return <ListMusic className="h-5 w-5" aria-hidden />;
    case 'media':
      return <Clapperboard className="h-5 w-5" aria-hidden />;
  }
}

function trustedCanonicalRoute(route: string): string | null {
  const allowedPrefixes = ['/music/songs/', '/music/artists/', '/music/playlists/', '/media/library/'];
  if (!route.startsWith('/') || route.startsWith('//') || !allowedPrefixes.some((prefix) => route.startsWith(prefix))) {
    return null;
  }

  return route;
}

function ResultArtwork({ item }: { item: SearchResultItem }) {
  const [imageFailed, setImageFailed] = useState(false);

  if (!item.artworkUrl || imageFailed) {
    return (
      <span className="bg-soft text-accent-content flex h-12 w-12 shrink-0 items-center justify-center rounded-xl sm:h-14 sm:w-14">
        <EntityIcon entityType={item.entityType} />
      </span>
    );
  }

  return (
    <img
      src={item.artworkUrl}
      alt=""
      className="bg-soft h-12 w-12 shrink-0 rounded-xl object-cover sm:h-14 sm:w-14"
      onError={() => setImageFailed(true)}
    />
  );
}

function ResultRow({ item }: { item: SearchResultItem }) {
  const canonicalRoute = trustedCanonicalRoute(item.canonicalRoute);
  const content = (
    <>
      <ResultArtwork item={item} />
      <span className="min-w-0 flex-1">
        <span className="text-ink block truncate text-sm font-black sm:text-base">{item.title}</span>
        <span className="text-muted mt-0.5 block truncate text-xs font-semibold sm:text-sm">
          {[item.subtitle, item.detail].filter(Boolean).join(' · ') || item.entityType}
        </span>
      </span>
      <span className="bg-soft text-accent-content hidden rounded-full px-2.5 py-1 text-[0.68rem] font-black capitalize sm:inline">
        {item.entityType}
      </span>
      {canonicalRoute ? <ArrowRight className="text-muted group-hover:text-accent-content h-4 w-4 shrink-0 transition" aria-hidden /> : null}
    </>
  );

  if (!canonicalRoute) {
    return (
      <div className="flex min-w-0 items-center gap-3 rounded-xl px-2 py-2 opacity-70" title="This result does not have a safe Cantaro destination yet">
        {content}
      </div>
    );
  }

  return (
    <Link
      to={canonicalRoute as never}
      className="group hover:bg-soft flex min-w-0 items-center gap-3 rounded-xl px-2 py-2 transition focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
    >
      {content}
    </Link>
  );
}

function GroupLoadingState() {
  return (
    <div className="space-y-2" role="status" aria-label="Loading search results">
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex animate-pulse items-center gap-3 rounded-xl px-2 py-2">
          <span className="bg-soft h-12 w-12 shrink-0 rounded-xl sm:h-14 sm:w-14" />
          <span className="min-w-0 flex-1 space-y-2">
            <span className="bg-soft block h-3 w-2/5 rounded-full" />
            <span className="bg-soft block h-2.5 w-1/4 rounded-full" />
          </span>
        </div>
      ))}
    </div>
  );
}

function GroupMessage({
  icon: Icon,
  title,
  detail,
  action,
}: {
  icon: LucideIcon;
  title: string;
  detail: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="bg-soft flex items-start gap-3 rounded-xl px-4 py-4">
      <span className="bg-panel-solid text-accent-content flex h-9 w-9 shrink-0 items-center justify-center rounded-xl">
        <Icon className="h-4 w-4" aria-hidden />
      </span>
      <div className="min-w-0">
        <p className="text-ink text-sm font-black">{title}</p>
        <p className="text-muted mt-1 text-sm leading-5 font-medium">{detail}</p>
        {action ? <div className="mt-3">{action}</div> : null}
      </div>
    </div>
  );
}

function SuccessfulGroupResults({
  group,
  presentation,
  query,
  showGroupLink,
}: {
  group: SearchResultGroup;
  presentation: SearchGroupPresentation;
  query: string;
  showGroupLink: boolean;
}) {
  if (group.items.length === 0) {
    return (
      <GroupMessage
        icon={Search}
        title={`No matching ${presentation.label.toLowerCase()}`}
        detail={`Nothing in your archive matched “${query}” in this group.`}
      />
    );
  }

  return (
    <>
      <div className="divide-line divide-y">
        {group.items.map((item) => <ResultRow key={`${item.entityType}-${item.id}`} item={item} />)}
      </div>
      {group.hasMore && !showGroupLink ? (
        <p className="text-muted px-2 pt-3 text-xs font-semibold">
          More matches exist. Refine your search to narrow the list.
        </p>
      ) : null}
    </>
  );
}

function SearchGroupResultState({
  group,
  presentation,
  query,
  showGroupLink,
  onRetry,
}: {
  group: SearchResultGroup;
  presentation: SearchGroupPresentation;
  query: string;
  showGroupLink: boolean;
  onRetry: () => void;
}) {
  if (group.status === 'unavailable') {
    return (
      <GroupMessage
        icon={Library}
        title={`${presentation.label} search is coming later`}
        detail={group.message || `Your ${presentation.singular} results will appear here when this part of the archive is ready.`}
      />
    );
  }

  if (group.status === 'failed') {
    return (
      <GroupMessage
        icon={AlertTriangle}
        title={`${presentation.label} could not be searched`}
        detail={group.message || 'The rest of your results are still available.'}
        action={(
          <button
            type="button"
            className="bg-panel-solid text-ink hover:text-accent-content inline-flex items-center gap-2 rounded-xl px-3 py-2 text-xs font-black transition focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
            onClick={onRetry}
          >
            <RotateCcw className="h-3.5 w-3.5" aria-hidden />
            Try again
          </button>
        )}
      />
    );
  }

  return (
    <SuccessfulGroupResults
      group={group}
      presentation={presentation}
      query={query}
      showGroupLink={showGroupLink}
    />
  );
}

function SearchGroupBody({
  group,
  loading,
  presentation,
  query,
  showGroupLink,
  onRetry,
}: {
  group?: SearchResultGroup;
  loading: boolean;
  presentation: SearchGroupPresentation;
  query: string;
  showGroupLink: boolean;
  onRetry: () => void;
}) {
  if (loading || !group) return <GroupLoadingState />;
  return (
    <SearchGroupResultState
      group={group}
      presentation={presentation}
      query={query}
      showGroupLink={showGroupLink}
      onRetry={onRetry}
    />
  );
}

function SearchGroupSection({
  presentation,
  group,
  loading,
  query,
  showGroupLink,
  onRetry,
}: {
  presentation: SearchGroupPresentation;
  group?: SearchResultGroup;
  loading: boolean;
  query: string;
  showGroupLink: boolean;
  onRetry: () => void;
}) {
  const Icon = presentation.icon;

  return (
    <section className="bg-panel rounded-2xl p-3 sm:p-4" aria-labelledby={`search-group-${presentation.id}`}>
      <header className="mb-2 flex items-start justify-between gap-3 px-2 pt-1">
        <div className="flex min-w-0 items-start gap-3">
          <span className="bg-soft text-accent-content mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-xl">
            <Icon className="h-4 w-4" aria-hidden />
          </span>
          <div className="min-w-0">
            <h2 id={`search-group-${presentation.id}`} className="text-ink text-base font-black">{presentation.label}</h2>
            <p className="text-muted mt-0.5 text-xs font-semibold">{presentation.description}</p>
          </div>
        </div>
        {showGroupLink && group?.status === 'ok' && (group.items.length > 0 || group.hasMore) ? (
          <Link
            to="/search"
            search={{ q: query, group: presentation.id }}
            className="text-accent-content hover:bg-soft shrink-0 rounded-xl px-2.5 py-1.5 text-xs font-black transition focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none"
          >
            View all
          </Link>
        ) : null}
      </header>

      <SearchGroupBody
        group={group}
        loading={loading}
        presentation={presentation}
        query={query}
        showGroupLink={showGroupLink}
        onRetry={onRetry}
      />
    </section>
  );
}

function SearchTabs({ activeGroup, query }: { activeGroup: SearchGroupId; query: string }) {
  const tabs: Array<{ id: SearchGroupId; label: string }> = [
    { id: 'all', label: 'All' },
    ...searchGroups.map(({ id, label }) => ({ id, label })),
  ];

  return (
    <nav className="-mx-1 overflow-x-auto px-1 pb-1" aria-label="Search result groups">
      <div className="bg-soft flex min-w-max gap-1 rounded-2xl p-1">
        {tabs.map((tab) => {
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
        Use the search field above to find songs, playlists, artists, and watched or saved media across Cantaro.
      </p>
    </div>
  );
}

function WholeSearchError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="bg-panel rounded-2xl px-5 py-10 text-center sm:px-8">
      <span className="bg-error-surface text-error-content mx-auto flex h-12 w-12 items-center justify-center rounded-2xl">
        <AlertTriangle className="h-5 w-5" aria-hidden />
      </span>
      <h2 className="text-ink mt-4 text-xl font-black">Search could not be completed</h2>
      <p className="text-muted mx-auto mt-2 max-w-lg text-sm leading-6 font-medium">{message}</p>
      <button
        type="button"
        className="bg-action text-action-content hover:bg-action-hover mt-5 inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-black transition focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 focus-visible:outline-none"
        onClick={onRetry}
      >
        <RotateCcw className="h-4 w-4" aria-hidden />
        Try again
      </button>
    </div>
  );
}

function SearchResults({
  normalizedQuery,
  error,
  visibleGroups,
  response,
  loading,
  activeGroup,
  onRetry,
}: {
  normalizedQuery: string;
  error: string | null;
  visibleGroups: SearchGroupPresentation[];
  response: SearchResponse | null;
  loading: boolean;
  activeGroup: SearchGroupId;
  onRetry: () => void;
}) {
  if (!normalizedQuery) return <EmptyQueryState />;
  if (error) return <WholeSearchError message={error} onRetry={onRetry} />;

  return (
    <div className="space-y-4">
      {visibleGroups.map((presentation) => (
        <SearchGroupSection
          key={presentation.id}
          presentation={presentation}
          group={response?.groups[presentation.id]}
          loading={loading}
          query={normalizedQuery}
          showGroupLink={activeGroup === 'all'}
          onRetry={onRetry}
        />
      ))}
    </div>
  );
}

export function SearchPage({ query, activeGroup }: SearchPageProps) {
  const normalizedQuery = query.trim();
  const [response, setResponse] = useState<SearchResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [retryKey, setRetryKey] = useState(0);

  useEffect(() => {
    if (!normalizedQuery) {
      setResponse(null);
      setError(null);
      setLoading(false);
      return;
    }

    const abortController = new AbortController();
    setLoading(true);
    setError(null);

    void searchCantaro(normalizedQuery, {
      limitPerGroup: getSearchResultLimit(activeGroup),
      signal: abortController.signal,
    })
      .then((result) => setResponse(result))
      .catch((reason: unknown) => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return;
        setResponse(null);
        setError(reason instanceof Error ? reason.message : 'Cantaro search is temporarily unavailable.');
      })
      .finally(() => {
        if (!abortController.signal.aborted) setLoading(false);
      });

    return () => abortController.abort();
  }, [activeGroup, normalizedQuery, retryKey]);

  const visibleGroups = activeGroup === 'all'
    ? searchGroups
    : searchGroups.filter((group) => group.id === activeGroup);

  return (
    <PageShell sidebar={<SearchSidebar />} contentClassName="search-page-content">
      <div className="mx-auto w-full max-w-5xl">
        <header className="mb-5">
          <h1 className="text-ink text-2xl font-black tracking-[-0.02em] sm:text-3xl">Search Cantaro</h1>
          <p className="text-muted mt-1 max-w-2xl text-sm leading-6 font-medium">
            {normalizedQuery ? `Results from your archive for “${normalizedQuery}”.` : 'Find something you have already saved, synced, or watched.'}
          </p>
        </header>

        <SearchTabs activeGroup={activeGroup} query={normalizedQuery} />

        <div className="mt-5">
          <SearchResults
            normalizedQuery={normalizedQuery}
            error={error}
            visibleGroups={visibleGroups}
            response={response}
            loading={loading}
            activeGroup={activeGroup}
            onRetry={() => setRetryKey((key) => key + 1)}
          />
        </div>
      </div>
    </PageShell>
  );
}
