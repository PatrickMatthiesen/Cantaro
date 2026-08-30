import { useCallback, useEffect, useRef, useState } from 'react';
import { GlassCard, GradientButton, GradientPageShell, PageLoadingState } from '@cantaro/client-shared/ui';
import { matchingApi } from '@cantaro/client-shared/music';
import type { KeyboardEvent, ReactNode } from 'react';
import type {
  MatchingQueueCandidateResponse,
  MatchingQueueFilters,
  MatchingQueueItemResponse,
  MatchingQueuePageResponse,
  MatchingQueuePlaylistResponse,
  MatchingSummaryResponse,
} from '@cantaro/client-shared/music';
import { SongGroupingReviewPanel } from './matching/SongGroupingReview';
import { useSongGroupingReview } from './matching/useSongGroupingReview';
import type { SongGroupingReviewState } from './matching/useSongGroupingReview';
import { CandidateList } from './matching/MatchingCandidateList';
import { formatDuration, formatPercent, formatSource, formatStatus, statusClasses } from './matching/matchingReviewPresentation';
import { MarkerList } from './matching/MarkerList';
import { MatchingQueueFiltersPanel } from './matching/MatchingQueueFiltersPanel';
import { TrackMatchingQueuePanel } from './matching/TrackMatchingQueue';

interface MatchingReviewPageProps {
  embedded?: boolean;
}

type MatchingActionHandler = (observationId: string, action: () => Promise<void>) => Promise<void>;
type MatchingReviewTab = 'musicbrainz' | 'work-queue' | 'track-groupings';

const matchingReviewTabs: ReadonlyArray<{ id: MatchingReviewTab; label: string }> = [
  { id: 'musicbrainz', label: 'MusicBrainz matches' },
  { id: 'work-queue', label: 'Matching queue' },
  { id: 'track-groupings', label: 'Track groupings' },
];

const matchingReviewTabOffsets: Readonly<Record<string, number>> = {
  ArrowRight: 1,
  ArrowDown: 1,
  ArrowLeft: -1,
  ArrowUp: -1,
};

function nextMatchingReviewTabIndex(key: string, currentIndex: number, tabCount: number): number | null {
  if (key === 'Home') return 0;
  if (key === 'End') return tabCount - 1;

  const offset = matchingReviewTabOffsets[key];
  return offset === undefined ? null : (currentIndex + offset + tabCount) % tabCount;
}

interface MatchingReviewState {
  activeObservationId: string | null;
  error: string | null;
  filters: MatchingQueueFilters;
  isRetryingFiltered: boolean;
  isLoading: boolean;
  loadQueue: () => Promise<MatchingQueuePageResponse | null>;
  page: number;
  pageData: MatchingQueuePageResponse | null;
  queue: MatchingQueueItemResponse[];
  runObservationAction: MatchingActionHandler;
  retryFiltered: () => Promise<void>;
  retryMessage: string | null;
  setFilters: (filters: MatchingQueueFilters) => void;
  summary: MatchingSummaryResponse | null;
  setPage: (page: number) => void;
}

function getErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

interface MatchingActionPosition {
  focusedElement: HTMLElement | null;
  scrollY: number;
}

function findObservationElement(observationId: string): HTMLElement | null {
  return Array.from(document.querySelectorAll<HTMLElement>('[data-matching-observation-id]'))
    .find((element) => element.dataset.matchingObservationId === observationId) ?? null;
}

function captureMatchingActionPosition(): MatchingActionPosition | null {
  if (typeof window === 'undefined' || typeof document === 'undefined') {
    return null;
  }

  return {
    focusedElement: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    scrollY: window.scrollY,
  };
}

function restoreMatchingActionPosition(
  position: MatchingActionPosition | null,
  fallbackObservationId: string | null,
) {
  if (!position || typeof window === 'undefined' || typeof document === 'undefined') {
    return;
  }

  window.requestAnimationFrame(() => {
    window.scrollTo({ behavior: 'auto', top: position.scrollY });
    const fallbackElement = fallbackObservationId ? findObservationElement(fallbackObservationId) : null;
    const focusTarget = position.focusedElement?.isConnected
      ? position.focusedElement
      : fallbackElement?.querySelector<HTMLElement>('button:not(:disabled)');
    focusTarget?.focus({ preventScroll: true });
  });
}

function useFilteredRetry(
  filters: MatchingQueueFilters,
  totalCount: number,
  loadQueue: () => Promise<MatchingQueuePageResponse | null>,
  setError: (error: string | null) => void,
) {
  const [isRetrying, setIsRetrying] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const clearMessage = useCallback(() => setMessage(null), []);
  const retry = useCallback(async () => {
    if (totalCount === 0 || !window.confirm(`Queue a new bounded retry cycle for ${totalCount} filtered item(s)?`)) return;
    setIsRetrying(true);
    setError(null);
    try {
      const result = await matchingApi.retryFiltered(filters);
      setMessage(`${result.queuedCount} item(s) queued for retry.`);
      await loadQueue();
    } catch (retryError) {
      setError(getErrorMessage(retryError, 'Failed to retry filtered matches'));
    } finally {
      setIsRetrying(false);
    }
  }, [filters, loadQueue, setError, totalCount]);
  return { clearMessage, isRetrying, message, retry };
}

function useMatchingQueueData(
  page: number,
  filters: MatchingQueueFilters,
  setPage: (page: number) => void,
  setError: (error: string | null) => void,
) {
  const [summary, setSummary] = useState<MatchingSummaryResponse | null>(null);
  const [queue, setQueue] = useState<MatchingQueueItemResponse[]>([]);
  const [pageData, setPageData] = useState<MatchingQueuePageResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const loadQueue = useCallback(async () => {
    try {
      const [summaryResponse, queueResponse] = await Promise.all([
        matchingApi.getSummary(), matchingApi.getQueue(page, 5, filters),
      ]);
      setSummary(summaryResponse);
      setQueue(queueResponse.items);
      setPageData(queueResponse);
      if (queueResponse.page !== page) setPage(queueResponse.page);
      setError(null);
      return queueResponse;
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load matching queue'));
      return null;
    } finally {
      setIsLoading(false);
    }
  }, [filters, page, setError, setPage]);
  useEffect(() => { void loadQueue(); }, [loadQueue]);
  return { isLoading, loadQueue, pageData, queue, summary };
}

function useMatchingReviewQueue(): MatchingReviewState {
  const [page, setPage] = useState(1);
  const [activeObservationId, setActiveObservationId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filters, setFiltersState] = useState<MatchingQueueFilters>({});

  const setFilters = useCallback((nextFilters: MatchingQueueFilters) => {
    setFiltersState(nextFilters);
    setPage(1);
  }, []);

  const data = useMatchingQueueData(page, filters, setPage, setError);

  const runObservationAction = useCallback(
    async (observationId: string, action: () => Promise<void>) => {
      const position = captureMatchingActionPosition();
      const observationIndex = data.queue.findIndex((item) => item.observationId === observationId);
      let fallbackObservationId: string | null = null;

      setActiveObservationId(observationId);
      try {
        await action();
        const queueResponse = await data.loadQueue();
        if (queueResponse && queueResponse.items.length > 0 && observationIndex >= 0) {
          fallbackObservationId = queueResponse.items[
            Math.min(observationIndex, queueResponse.items.length - 1)
          ].observationId;
        }
      } catch (actionError) {
        setError(getErrorMessage(actionError, 'Matching action failed'));
      } finally {
        setActiveObservationId(null);
        restoreMatchingActionPosition(position, fallbackObservationId);
      }
    },
    [data],
  );

  const filteredRetry = useFilteredRetry(filters, data.pageData?.totalCount ?? 0, data.loadQueue, setError);
  const updateFilters = useCallback((nextFilters: MatchingQueueFilters) => {
    filteredRetry.clearMessage();
    setFilters(nextFilters);
  }, [filteredRetry, setFilters]);

  return { activeObservationId, error, filters, isLoading: data.isLoading, isRetryingFiltered: filteredRetry.isRetrying, loadQueue: data.loadQueue, page, pageData: data.pageData, queue: data.queue, retryFiltered: filteredRetry.retry, retryMessage: filteredRetry.message, runObservationAction, setFilters: updateFilters, setPage, summary: data.summary };
}

function MatchingReviewLoading({ embedded }: { embedded: boolean }) {
  return embedded ? (
    <GlassCard className="p-6">
      <p className="text-sm text-content-muted">Loading matching review queue...</p>
    </GlassCard>
  ) : (
    <PageLoadingState message="Loading matching review queue..." />
  );
}

function MatchingReviewLayout({ children, embedded }: { children: ReactNode; embedded: boolean }) {
  return embedded ? (
    <div className="space-y-5">{children}</div>
  ) : (
    <GradientPageShell className="text-content">{children}</GradientPageShell>
  );
}

function MatchingReviewHeader({ onRefresh }: { onRefresh: () => void }) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-4 border-b border-border-subtle pb-6">
      <div>
        <h1 className="text-3xl font-black tracking-[-0.03em] text-content">Resolve track identity</h1>
        <p className="mt-2 text-sm text-content-muted">
          Review ambiguous and unmatched imports before they become canonical Cantaro tracks.
        </p>
      </div>
      <GradientButton tone="soft" onClick={onRefresh}>
        Refresh queue
      </GradientButton>
    </header>
  );
}

function getMatchingSummaryStats(summary: MatchingSummaryResponse) {
  return [
    { label: 'Unresolved', value: summary.totalUnresolved, tone: 'text-personal-accent-strong' },
    { label: 'Pending', value: summary.pending, tone: 'text-info-content' },
    { label: 'Ambiguous', value: summary.ambiguous, tone: 'text-warning-content' },
    { label: 'No match', value: summary.noMatch, tone: 'text-danger-content' },
  ];
}

function MatchingSummaryStats({ summary }: { summary: MatchingSummaryResponse | null }) {
  if (!summary) {
    return null;
  }

  return (
    <dl className="grid grid-cols-2 border-y border-border-subtle md:grid-cols-4">
      {getMatchingSummaryStats(summary).map((stat) => (
        <div key={stat.label} className="py-5 md:border-r md:border-border-subtle md:px-5 md:last:border-r-0">
          <dt className="text-sm text-content-muted">{stat.label}</dt>
          <dd className={`mt-2 text-3xl font-bold ${stat.tone}`}>
            {stat.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function MatchingReviewError({ error }: { error: string | null }) {
  if (!error) {
    return null;
  }

  return (
    <div className="border-y border-danger-border bg-danger-surface p-4 text-sm text-danger-content">
      {error}
    </div>
  );
}

function MatchingQueueEmptyState() {
  return (
    <div className="border-y border-border-subtle py-8">
      <h2 className="text-2xl font-semibold">Queue is clear</h2>
      <p className="mt-2 text-sm text-content-muted">
        Imported songs are either matched already or there are no playlists waiting for review.
      </p>
    </div>
  );
}

function MatchingQueueList({
  activeObservationId,
  onAction,
  queue,
}: {
  activeObservationId: string | null;
  onAction: MatchingActionHandler;
  queue: MatchingQueueItemResponse[];
}) {
  if (queue.length === 0) {
    return <MatchingQueueEmptyState />;
  }

  return (
    <section className="space-y-4">
      {queue.map((item) => (
        <QueueObservationItem
          key={item.observationId}
          item={item}
          activeObservationId={activeObservationId}
          onAction={onAction}
        />
      ))}
    </section>
  );
}

function MatchingQueuePagination({
  pageData,
  onPageChange,
  label = 'Matching queue',
}: {
  pageData: Pick<MatchingQueuePageResponse, 'page' | 'pageSize' | 'totalCount' | 'totalPages'> | null;
  onPageChange: (page: number) => void;
  label?: string;
}) {
  if (!pageData || pageData.totalPages <= 1) return null;
  const firstItem = (pageData.page - 1) * pageData.pageSize + 1;
  const lastItem = Math.min(pageData.page * pageData.pageSize, pageData.totalCount);
  return <nav className="mt-3 flex flex-wrap items-center justify-between gap-3 border-y border-border-subtle py-3" aria-label={`${label} pages`}><p className="text-sm font-medium text-content-muted">Reviewing {firstItem}–{lastItem} of {pageData.totalCount}</p><div className="flex items-center gap-2"><button type="button" disabled={pageData.page <= 1} onClick={() => onPageChange(pageData.page - 1)} className="min-h-10 border border-border-strong bg-surface px-3 text-sm font-semibold text-content transition-colors hover:bg-surface-hover disabled:cursor-not-allowed disabled:opacity-40">Previous</button><span className="min-w-20 text-center text-sm text-content-muted">Page {pageData.page} of {pageData.totalPages}</span><button type="button" disabled={pageData.page >= pageData.totalPages} onClick={() => onPageChange(pageData.page + 1)} className="min-h-10 bg-personal-accent px-3 text-sm font-semibold text-personal-accent-content transition-colors hover:bg-personal-accent-hover disabled:cursor-not-allowed disabled:opacity-40">Next</button></div></nav>;
}

function MatchingReviewTabs({
  activeTab,
  onTabChange,
}: {
  activeTab: MatchingReviewTab;
  onTabChange: (tab: MatchingReviewTab) => void;
}) {
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);

  const selectTab = (tab: MatchingReviewTab, focus = false) => {
    onTabChange(tab);
    if (focus) {
      tabRefs.current[matchingReviewTabs.findIndex((item) => item.id === tab)]?.focus();
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const currentIndex = matchingReviewTabs.findIndex((tab) => tab.id === activeTab);
    const nextIndex = nextMatchingReviewTabIndex(event.key, currentIndex, matchingReviewTabs.length);

    if (nextIndex !== null) {
      event.preventDefault();
      selectTab(matchingReviewTabs[nextIndex].id, true);
    }
  };

  return (
    <div className="overflow-x-auto">
      <div
        role="tablist"
        aria-label="Matching review type"
        className="inline-flex min-w-full border-b border-border-subtle sm:min-w-0"
      >
        {matchingReviewTabs.map((tab, index) => {
          const isActive = activeTab === tab.id;
          return (
            <button
              key={tab.id}
              ref={(element) => {
                tabRefs.current[index] = element;
              }}
              type="button"
              role="tab"
              id={`${tab.id}-tab`}
              aria-controls={`${tab.id}-panel`}
              aria-selected={isActive}
              tabIndex={isActive ? 0 : -1}
              onClick={() => selectTab(tab.id)}
              onKeyDown={handleKeyDown}
              className={`min-h-11 flex-1 border-b-2 px-4 py-2.5 text-sm font-bold whitespace-nowrap transition-colors focus-visible:outline-2 focus-visible:outline-focus ${
                isActive
                  ? 'border-personal-accent text-content'
                  : 'border-transparent text-content-muted hover:bg-surface-hover hover:text-content'
              }`}
            >
              {tab.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function MatchingReviewContent({
  groupingReview,
  review,
}: {
  groupingReview: SongGroupingReviewState;
  review: MatchingReviewState;
}) {
  const [activeTab, setActiveTab] = useState<MatchingReviewTab>('musicbrainz');

  return (
    <>
      <MatchingReviewHeader onRefresh={() => {
        void review.loadQueue();
        void groupingReview.loadSuggestions();
      }} />
      <MatchingReviewTabs activeTab={activeTab} onTabChange={setActiveTab} />
      {activeTab === 'musicbrainz' ? (
        <section id="musicbrainz-panel" role="tabpanel" aria-labelledby="musicbrainz-tab" className="space-y-5">
          <MatchingSummaryStats summary={review.summary} />
          <MatchingQueueFiltersPanel filters={review.filters} filteredCount={review.pageData?.totalCount ?? 0} isRetrying={review.isRetryingFiltered} retryMessage={review.retryMessage} onChange={review.setFilters} onRetry={() => void review.retryFiltered()} />
          <MatchingReviewError error={review.error} />
          <MatchingQueuePagination pageData={review.pageData} onPageChange={review.setPage} label="MusicBrainz matches" />
          <MatchingQueueList
            activeObservationId={review.activeObservationId}
            onAction={review.runObservationAction}
            queue={review.queue}
          />
          <MatchingQueuePagination pageData={review.pageData} onPageChange={review.setPage} label="MusicBrainz matches" />
        </section>
      ) : activeTab === 'work-queue' ? (
        <section id="work-queue-panel" role="tabpanel" aria-labelledby="work-queue-tab">
          <TrackMatchingQueuePanel />
        </section>
      ) : (
        <section id="track-groupings-panel" role="tabpanel" aria-labelledby="track-groupings-tab">
          <SongGroupingReviewPanel
            review={groupingReview}
            pagination={(
              <MatchingQueuePagination
                pageData={groupingReview.pageData}
                onPageChange={groupingReview.setPage}
                label="Song grouping review"
              />
            )}
          />
        </section>
      )}
    </>
  );
}

export function MatchingReviewPage({ embedded = false }: MatchingReviewPageProps) {
  const review = useMatchingReviewQueue();
  const groupingReview = useSongGroupingReview();

  if (review.isLoading) {
    return <MatchingReviewLoading embedded={embedded} />;
  }

  return (
    <MatchingReviewLayout embedded={embedded}>
      <MatchingReviewContent groupingReview={groupingReview} review={review} />
    </MatchingReviewLayout>
  );
}

interface QueueObservationItemProps {
  item: MatchingQueueItemResponse;
  activeObservationId: string | null;
  onAction: MatchingActionHandler;
}

function ObservationArtwork({ item }: { item: MatchingQueueItemResponse }) {
  if (item.thumbnailUrl) {
    return <img src={item.thumbnailUrl} alt="" className="h-28 w-28 shrink-0 object-cover" />;
  }

  return (
    <div className="flex h-28 w-28 shrink-0 items-center justify-center bg-surface-subtle text-sm text-content-muted">
      No art
    </div>
  );
}

function ObservationAttemptMeta({ item }: { item: MatchingQueueItemResponse }) {
  const lastAttempt = item.lastMatchAttemptedAt
    ? ` - Last tried ${new Date(item.lastMatchAttemptedAt).toLocaleString()}`
    : '';

  return (
    <p className="mt-2 text-xs text-content-muted">
      Attempts: {item.matchAttemptCount}
      {lastAttempt}
    </p>
  );
}

function ObservationDiagnostics({ item }: { item: MatchingQueueItemResponse }) {
  return (
    <div className="mt-3 grid gap-2 sm:grid-cols-3">
      <DiagnosticTile label="Top cluster" value={formatPercent(item.diagnostics.topScore) ?? '-'} />
      <DiagnosticTile label="Runner-up" value={formatPercent(item.diagnostics.secondDistinctScore) ?? '-'} />
      <DiagnosticTile label="Distinct clusters" value={item.diagnostics.distinctClusterCount} />
    </div>
  );
}

function DiagnosticTile({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="border-t border-border-subtle py-2">
      <p className="text-xs text-content-muted">{label}</p>
      <p className="mt-1 text-lg font-semibold text-content">{value}</p>
    </div>
  );
}

function ObservationSummary({ item }: { item: MatchingQueueItemResponse }) {
  const duration = formatDuration(item.durationSeconds);

  return (
    <div className="min-w-0 flex-1">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="text-xl font-semibold text-content">{item.title}</h2>
        <span className={`px-3 py-1 text-xs font-semibold ${statusClasses(item.matchStatus)}`}>
          {formatStatus(item.matchStatus)}
        </span>
      </div>
      <p className="mt-2 text-sm text-content-muted">
        {item.artist ?? 'Unknown artist'} {duration ? `- ${duration}` : ''}
      </p>
      <p className="mt-2 text-xs text-content-muted">Source: {formatSource(item.sourceType, item.externalId)}</p>
      <p className="mt-2 text-sm text-content">{item.resolutionNotes ?? 'Waiting for a matching decision.'}</p>
      <MarkerList markers={item.diagnostics.versionMarkers} tone="version" />
      <MarkerList markers={item.diagnostics.playbackModifiers} tone="playback" />
      {item.lastMatchError ? <p className="mt-2 text-sm text-rose-700">Last error: {item.lastMatchError}</p> : null}
      <ObservationAttemptMeta item={item} />
      <ObservationDiagnostics item={item} />
    </div>
  );
}

function ObservationActions({
  isBusy,
  item,
  onAction,
}: {
  isBusy: boolean;
  item: MatchingQueueItemResponse;
  onAction: MatchingActionHandler;
}) {
  return (
    <div className="flex flex-wrap gap-2 lg:w-64 lg:flex-col">
      <GradientButton
        className="flex-1 lg:flex-none"
        onClick={() => void onAction(item.observationId, () => matchingApi.retry(item.observationId))}
        disabled={isBusy}
      >
        {isBusy ? 'Working...' : 'Retry matching'}
      </GradientButton>
      <GradientButton
        tone="soft"
        className="flex-1 lg:flex-none"
        onClick={() => void onAction(item.observationId, () => matchingApi.createTrack(item.observationId))}
        disabled={isBusy}
      >
        Create canonical track
      </GradientButton>
      <button
        type="button"
        className="min-h-11 flex-1 px-5 text-sm font-semibold text-content transition-colors hover:bg-surface-hover disabled:cursor-not-allowed disabled:opacity-60 lg:flex-none"
        onClick={() => void onAction(item.observationId, () => matchingApi.markNoMatch(item.observationId))}
        disabled={isBusy}
      >
        Mark no match
      </button>
    </div>
  );
}

function PlaylistAppearances({ playlists }: { playlists: MatchingQueuePlaylistResponse[] }) {
  return (
    <div className="flex min-w-0 flex-col items-start gap-2 py-1 text-content-muted sm:flex-row sm:items-center">
      <p className="shrink-0 text-[0.65rem] font-black tracking-wider uppercase">Appears in</p>
      <ul className="flex min-w-0 flex-wrap gap-x-3 gap-y-1">
        {playlists.map((playlist) => (
          <li key={`${playlist.playlistId}-${playlist.position}`} className="inline-flex min-w-0 items-baseline gap-1 text-xs font-semibold text-content">
            <span className="truncate">{playlist.playlistName}</span>
            <span className="text-content-subtle">#{playlist.position + 1}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function CandidateSection({
  candidates,
  disabled,
  observationId,
  onAction,
  suggestedVersionFlags,
}: {
  candidates: MatchingQueueCandidateResponse[];
  disabled: boolean;
  observationId: string;
  onAction: MatchingActionHandler;
  suggestedVersionFlags: number;
}) {
  return (
    <div className="border-t border-border-subtle pt-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs tracking-[0.2em] text-content-muted uppercase">Suggested candidates</p>
        <span className="bg-surface-subtle px-2 py-1 text-[11px] font-semibold text-content-muted">
          {candidates.length} stored
        </span>
      </div>
      <CandidateList
        candidates={candidates}
        disabled={disabled}
        observationId={observationId}
        onAction={onAction}
        suggestedVersionFlags={suggestedVersionFlags}
      />
    </div>
  );
}

function QueueObservationItem({ item, activeObservationId, onAction }: QueueObservationItemProps) {
  const isBusy = activeObservationId === item.observationId;

  return (
    <GlassCard className="border-x-0 p-5" data-matching-observation-id={item.observationId}>
      <div className="flex flex-col gap-4 lg:flex-row">
        <div className="flex min-w-0 flex-1 gap-4">
          <ObservationArtwork item={item} />
          <ObservationSummary item={item} />
        </div>
        <ObservationActions isBusy={isBusy} item={item} onAction={onAction} />
      </div>

      <div className="mt-4 space-y-3">
        <PlaylistAppearances playlists={item.playlists} />
        <CandidateSection
          candidates={item.candidates}
          disabled={isBusy}
          observationId={item.observationId}
          onAction={onAction}
          suggestedVersionFlags={item.suggestedVersionFlags}
        />
      </div>
    </GlassCard>
  );
}
