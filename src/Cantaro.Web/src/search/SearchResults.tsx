import { Link } from '@tanstack/react-router';
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
import { useState, type ReactNode } from 'react';
import type {
  SearchEntityType,
  SearchResponse,
  SearchResultGroup,
  SearchResultGroupId,
  SearchResultItem,
} from './searchApi';
import { searchGroups, type SearchGroupPresentation } from './searchGroups';
import { rankSearchResults } from './searchRanking';
import { searchResultDomId, trustedCanonicalRoute } from './searchRouting';

type SearchNavigateHandler = (route: string) => boolean | void;

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

function SearchGroupIcon({ groupId }: { groupId: SearchResultGroupId }) {
  switch (groupId) {
    case 'songs':
      return <Music2 className="h-4 w-4" aria-hidden />;
    case 'artists':
      return <UserRound className="h-4 w-4" aria-hidden />;
    case 'playlists':
      return <ListMusic className="h-4 w-4" aria-hidden />;
    case 'media':
      return <Clapperboard className="h-4 w-4" aria-hidden />;
  }
}

function ResultArtwork({ item, compact }: { item: SearchResultItem; compact: boolean }) {
  const [imageFailed, setImageFailed] = useState(false);
  const sizeClass = compact ? 'h-10 w-10' : 'h-12 w-12 sm:h-14 sm:w-14';

  if (!item.artworkUrl || imageFailed) {
    return (
      <span className={`flex shrink-0 items-center justify-center bg-surface-subtle text-personal-accent-strong ${sizeClass}`}>
        <EntityIcon entityType={item.entityType} />
      </span>
    );
  }

  return (
    <img
      src={item.artworkUrl}
      alt=""
      className={`shrink-0 bg-surface-subtle object-cover ${sizeClass}`}
      onError={() => setImageFailed(true)}
    />
  );
}

function ResultContent({
  item,
  compact,
  showArrow,
  showEntityType = false,
}: {
  item: SearchResultItem;
  compact: boolean;
  showArrow: boolean;
  showEntityType?: boolean;
}) {
  const metadata = resultMetadata(item, showEntityType);
  const titleClassName = compact ? 'text-sm' : 'text-sm sm:text-base';
  const metadataClassName = compact ? 'text-xs' : 'text-xs sm:text-sm';

  return (
    <>
      <ResultArtwork item={item} compact={compact} />
      <span className="min-w-0 flex-1">
        <span className={`block truncate font-black text-content ${titleClassName}`}>{item.title}</span>
        <span className={`mt-0.5 block truncate font-semibold text-content-muted ${metadataClassName}`}>
          {metadata || item.entityType}
        </span>
      </span>
      {!compact ? (
        <span className="hidden bg-surface-subtle px-2.5 py-1 text-[0.68rem] font-semibold text-content-muted capitalize sm:inline">
          {item.entityType}
        </span>
      ) : null}
      {showArrow ? <ArrowRight className="h-4 w-4 shrink-0 text-content-muted transition group-hover:text-accent-strong" aria-hidden /> : null}
    </>
  );
}

function resultMetadata(item: SearchResultItem, showEntityType: boolean): string {
  const entityType = showEntityType ? item.entityType : undefined;
  return [entityType, item.subtitle, item.detail].filter(Boolean).join(' · ');
}

function SearchResultRow({
  item,
  compact = false,
  onNavigate,
  asOption = false,
  showEntityType = false,
}: {
  item: SearchResultItem;
  compact?: boolean;
  onNavigate?: SearchNavigateHandler;
  asOption?: boolean;
  showEntityType?: boolean;
}) {
  const canonicalRoute = trustedCanonicalRoute(item.canonicalRoute);

  if (!canonicalRoute) {
    return (
      <div className="flex min-w-0 items-center gap-3 px-2 py-2 opacity-70" title="This result does not have a safe Cantaro destination yet">
        <ResultContent item={item} compact={compact} showArrow={false} showEntityType={showEntityType} />
      </div>
    );
  }

  return (
    <Link
      to={canonicalRoute as never}
      data-search-result
      id={asOption ? searchResultDomId(item) : undefined}
      role={asOption ? 'option' : undefined}
      aria-selected={asOption ? false : undefined}
      className="group flex min-w-0 items-center gap-3 px-2 py-2 transition-colors hover:bg-surface-hover focus-visible:outline-2 focus-visible:outline-focus data-[active=true]:bg-surface-hover"
      onClick={(event) => {
        if (onNavigate?.(canonicalRoute)) event.preventDefault();
      }}
    >
      <ResultContent item={item} compact={compact} showArrow showEntityType={showEntityType} />
    </Link>
  );
}

function SearchLoadingRows({ compact = false }: { compact?: boolean }) {
  return (
    <div className="global-search-loading space-y-1" role="status" aria-label="Loading search results">
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex animate-pulse items-center gap-3 px-2 py-2">
          <span className={`shrink-0 bg-surface-subtle ${compact ? 'h-10 w-10' : 'h-12 w-12 sm:h-14 sm:w-14'}`} />
          <span className="min-w-0 flex-1 space-y-2">
            <span className="block h-3 w-2/5 rounded-full bg-surface-subtle" />
            <span className="block h-2.5 w-1/4 rounded-full bg-surface-subtle" />
          </span>
        </div>
      ))}
    </div>
  );
}

function SearchStatusMessage({
  icon: Icon,
  title,
  detail,
  action,
  compact = false,
}: {
  icon: LucideIcon;
  title: string;
  detail: string;
  action?: ReactNode;
  compact?: boolean;
}) {
  return (
    <div className={`flex items-start gap-3 border-y border-border-subtle ${compact ? 'px-3 py-3' : 'px-4 py-4'}`}>
      <span className="flex h-9 w-9 shrink-0 items-center justify-center bg-surface text-personal-accent-strong">
        <Icon className="h-4 w-4" aria-hidden />
      </span>
      <div className="min-w-0">
        <p className="text-sm font-black text-content">{title}</p>
        <p className="mt-1 text-sm leading-5 font-medium text-content-muted">{detail}</p>
        {action ? <div className="mt-3">{action}</div> : null}
      </div>
    </div>
  );
}

function SuccessfulGroup({
  group,
  presentation,
  query,
  compact,
  onNavigate,
  asListbox,
}: {
  group: SearchResultGroup;
  presentation: SearchGroupPresentation;
  query: string;
  compact: boolean;
  onNavigate?: SearchNavigateHandler;
  asListbox: boolean;
}) {
  if (group.items.length === 0) {
    return (
      <div className="space-y-2">
        <SearchStatusMessage
          icon={Search}
          title={`No matching ${presentation.label.toLowerCase()}`}
          detail={`Nothing matched “${query}” in this group.`}
          compact={compact}
        />
        {group.message ? (
          <SearchStatusMessage
            icon={AlertTriangle}
            title="Some results may be missing"
            detail={group.message}
            compact={compact}
          />
        ) : null}
      </div>
    );
  }

  return (
    <div>
      <div className="divide-y divide-border-subtle">
        {group.items.map((item) => (
          <SearchResultRow
            key={`${item.entityType}-${item.id}`}
            item={item}
            compact={compact}
            onNavigate={onNavigate}
            asOption={asListbox}
          />
        ))}
      </div>
      {group.message ? (
        <div className="mt-2">
          <SearchStatusMessage
            icon={AlertTriangle}
            title="Some results may be missing"
            detail={group.message}
            compact={compact}
          />
        </div>
      ) : null}
    </div>
  );
}

function ResolvedGroup({
  group,
  presentation,
  query,
  compact,
  onRetry,
  onNavigate,
  asListbox,
}: {
  group: SearchResultGroup;
  presentation: SearchGroupPresentation;
  query: string;
  compact: boolean;
  onRetry: () => void;
  onNavigate?: SearchNavigateHandler;
  asListbox: boolean;
}) {
  if (group.status === 'unavailable') {
    return (
      <SearchStatusMessage
        icon={Library}
        title={`${presentation.label} search is coming later`}
        detail={group.message || `Your ${presentation.singular} results will appear here when ready.`}
        compact={compact}
      />
    );
  }

  if (group.status === 'failed') {
    return (
      <SearchStatusMessage
        icon={AlertTriangle}
        title={`${presentation.label} could not be searched`}
        detail={group.message || 'The rest of your results are still available.'}
        compact={compact}
        action={(
          <button
            type="button"
            className="inline-flex min-h-10 items-center gap-2 border border-border-strong bg-surface px-3 text-xs font-black text-content transition hover:bg-surface-hover hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
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
    <SuccessfulGroup
      group={group}
      presentation={presentation}
      query={query}
      compact={compact}
      onNavigate={onNavigate}
      asListbox={asListbox}
    />
  );
}

function SearchGroup({
  presentation,
  group,
  loading,
  query,
  compact,
  showGroupLink,
  onRetry,
  onNavigate,
  asListbox,
  idPrefix,
}: {
  presentation: SearchGroupPresentation;
  group?: SearchResultGroup;
  loading: boolean;
  query: string;
  compact: boolean;
  showGroupLink: boolean;
  onRetry: () => void;
  onNavigate?: SearchNavigateHandler;
  asListbox: boolean;
  idPrefix: string;
}) {
  return (
    <section
      className={compact ? 'py-2' : 'border-b border-border-subtle py-4'}
      aria-labelledby={`${idPrefix}-group-${presentation.id}`}
      role={asListbox ? 'group' : undefined}
    >
      <SearchGroupHeader
        presentation={presentation}
        group={group}
        query={query}
        compact={compact}
        showGroupLink={showGroupLink}
        onNavigate={onNavigate}
        idPrefix={idPrefix}
      />
      <SearchGroupBody
        presentation={presentation}
        group={group}
        loading={loading}
        query={query}
        compact={compact}
        onRetry={onRetry}
        onNavigate={onNavigate}
        asListbox={asListbox}
      />
    </section>
  );
}

function SearchGroupHeader({
  presentation,
  group,
  query,
  compact,
  showGroupLink,
  onNavigate,
  idPrefix,
}: {
  presentation: SearchGroupPresentation;
  group?: SearchResultGroup;
  query: string;
  compact: boolean;
  showGroupLink: boolean;
  onNavigate?: SearchNavigateHandler;
  idPrefix: string;
}) {
  const hasGroupLink = shouldShowGroupLink(showGroupLink, group);

  return (
    <header className={`flex items-start justify-between gap-3 px-2 ${compact ? 'mb-1' : 'mb-2 pt-1'}`}>
      <div className="flex min-w-0 items-start gap-3">
        <span className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center bg-surface-subtle text-personal-accent-strong">
          <SearchGroupIcon groupId={presentation.id} />
        </span>
        <div className="min-w-0">
          <h2 id={`${idPrefix}-group-${presentation.id}`} className="text-sm font-black text-content sm:text-base">{presentation.label}</h2>
          {!compact ? <p className="mt-0.5 text-xs font-semibold text-content-muted">{presentation.description}</p> : null}
        </div>
      </div>
      {hasGroupLink ? (
        <Link
          to="/search"
          search={{ q: query, group: presentation.id }}
          className="shrink-0 px-2.5 py-1.5 text-xs font-bold text-content-muted transition-colors hover:bg-surface-hover hover:text-personal-accent-strong focus-visible:outline-2 focus-visible:outline-focus"
          onClick={(event) => {
            if (onNavigate?.('/search')) event.preventDefault();
          }}
        >
          View all
        </Link>
      ) : null}
    </header>
  );
}

function shouldShowGroupLink(showGroupLink: boolean, group?: SearchResultGroup): boolean {
  if (!showGroupLink || group?.status !== 'ok') return false;
  return group.items.length > 0 || group.hasMore;
}

function SearchGroupBody({
  presentation,
  group,
  loading,
  query,
  compact,
  onRetry,
  onNavigate,
  asListbox,
}: {
  presentation: SearchGroupPresentation;
  group?: SearchResultGroup;
  loading: boolean;
  query: string;
  compact: boolean;
  onRetry: () => void;
  onNavigate?: SearchNavigateHandler;
  asListbox: boolean;
}) {
  if (loading || !group) return <SearchLoadingRows compact={compact} />;
  return (
    <ResolvedGroup
      group={group}
      presentation={presentation}
      query={query}
      compact={compact}
      onRetry={onRetry}
      onNavigate={onNavigate}
      asListbox={asListbox}
    />
  );
}

function WholeSearchError({ message, onRetry, compact }: { message: string; onRetry: () => void; compact: boolean }) {
  return (
    <SearchStatusMessage
      icon={AlertTriangle}
      title="Search could not be completed"
      detail={message}
      compact={compact}
      action={(
        <button
          type="button"
          className="inline-flex min-h-10 items-center gap-2 bg-personal-accent px-3 text-xs font-black text-personal-accent-content transition hover:bg-personal-accent-hover focus-visible:outline-2 focus-visible:outline-focus"
          onClick={onRetry}
        >
          <RotateCcw className="h-3.5 w-3.5" aria-hidden />
          Try again
        </button>
      )}
    />
  );
}

function searchWarnings(response: SearchResponse | null, groupIds: SearchResultGroupId[]): string[] {
  if (!response) return [];
  return Array.from(new Set(groupIds.flatMap((groupId) => {
    const group = response.groups[groupId];
    if (group.status === 'unavailable') return [];
    return group.message ? [group.message] : [];
  })));
}

function RankedResultsBody({
  query,
  results,
  error,
  loading,
  onRetry,
  onNavigate,
  asListbox,
}: {
  query: string;
  results: SearchResultItem[];
  error: string | null;
  loading: boolean;
  onRetry: () => void;
  onNavigate?: SearchNavigateHandler;
  asListbox: boolean;
}) {
  if (error) return <WholeSearchError message={error} onRetry={onRetry} compact />;
  if (loading) return <SearchLoadingRows compact />;
  if (results.length === 0) {
    return (
      <SearchStatusMessage
        icon={Search}
        title="No matching results"
        detail={`Nothing matched “${query}”.`}
        compact
      />
    );
  }

  return (
    <div className="divide-y divide-border-subtle">
      {results.map((item) => (
        <SearchResultRow
          key={`${item.entityType}-${item.id}`}
          item={item}
          compact
          onNavigate={onNavigate}
          asOption={asListbox}
          showEntityType
        />
      ))}
    </div>
  );
}

function RankedResultsWarning({ warnings, hasResults }: { warnings: string[]; hasResults: boolean }) {
  if (warnings.length === 0) return null;
  return (
    <div className={hasResults ? 'mt-2' : ''}>
      <SearchStatusMessage
        icon={AlertTriangle}
        title="Some results may be missing"
        detail={warnings.join(' ')}
        compact
      />
    </div>
  );
}

export function SearchRankedResults({
  query,
  response,
  error,
  loading,
  groupIds,
  onRetry,
  onNavigate,
  asListbox = false,
  listboxId,
  maxResults,
}: {
  query: string;
  response: SearchResponse | null;
  error: string | null;
  loading: boolean;
  groupIds: SearchResultGroupId[];
  onRetry: () => void;
  onNavigate?: SearchNavigateHandler;
  asListbox?: boolean;
  listboxId?: string;
  maxResults?: number;
}) {
  const results = rankSearchResults(response, groupIds, query, maxResults)
    .filter(({ item }) => trustedCanonicalRoute(item.canonicalRoute));
  const resultItems = results.map(({ item }) => item);
  const warnings = searchWarnings(response, groupIds);

  return (
    <div id={listboxId} role={asListbox ? 'listbox' : undefined} className="py-2">
      <RankedResultsBody
        query={query}
        results={resultItems}
        error={error}
        loading={loading}
        onRetry={onRetry}
        onNavigate={onNavigate}
        asListbox={asListbox}
      />
      {!error && !loading ? <RankedResultsWarning warnings={warnings} hasResults={resultItems.length > 0} /> : null}
    </div>
  );
}

export function SearchGroupedResults({
  query,
  response,
  error,
  loading,
  groupIds,
  compact = false,
  showGroupLinks = false,
  onRetry,
  onNavigate,
  asListbox = false,
  listboxId,
  idPrefix = 'search',
}: {
  query: string;
  response: SearchResponse | null;
  error: string | null;
  loading: boolean;
  groupIds: SearchResultGroupId[];
  compact?: boolean;
  showGroupLinks?: boolean;
  onRetry: () => void;
  onNavigate?: SearchNavigateHandler;
  asListbox?: boolean;
  listboxId?: string;
  idPrefix?: string;
}) {
  return (
    <div
      id={listboxId}
      role={asListbox ? 'listbox' : undefined}
      className={compact ? 'divide-y divide-border-subtle' : 'space-y-4'}
    >
      {error ? <WholeSearchError message={error} onRetry={onRetry} compact={compact} /> : (
        searchGroups
          .filter((presentation) => groupIds.includes(presentation.id))
          .map((presentation) => (
            <SearchGroup
              key={presentation.id}
              presentation={presentation}
              group={response?.groups[presentation.id]}
              loading={loading}
              query={query}
              compact={compact}
              showGroupLink={showGroupLinks}
              onRetry={onRetry}
              onNavigate={onNavigate}
              asListbox={asListbox}
              idPrefix={idPrefix}
            />
          ))
      )}
    </div>
  );
}
