import { useCallback, useEffect, useRef, useState } from 'react';
import { GlassCard, GradientButton, GradientPageShell, PageLoadingState } from '@cantaro/client-shared/ui';
import { matchingApi } from '@cantaro/client-shared/music';
import type { KeyboardEvent, ReactNode } from 'react';
import type {
  MatchingCandidateComparisonResponse,
  MatchingQueueCandidateResponse,
  MatchingQueueItemResponse,
  MatchingQueuePageResponse,
  MatchingQueuePlaylistResponse,
  MatchingSummaryResponse,
  SongGroupingSuggestionPageResponse,
  SongGroupingSuggestionResponse,
  SongGroupingTrackResponse,
} from '@cantaro/client-shared/music';

interface MatchingReviewPageProps {
  embedded?: boolean;
}

type MatchingActionHandler = (observationId: string, action: () => Promise<void>) => Promise<void>;
type MatchingReviewTab = 'musicbrainz' | 'track-groupings';

const matchingReviewTabs: ReadonlyArray<{ id: MatchingReviewTab; label: string }> = [
  { id: 'musicbrainz', label: 'MusicBrainz matches' },
  { id: 'track-groupings', label: 'Track groupings' },
];

interface MatchingReviewState {
  activeObservationId: string | null;
  error: string | null;
  isLoading: boolean;
  loadQueue: () => Promise<MatchingQueuePageResponse | null>;
  page: number;
  pageData: MatchingQueuePageResponse | null;
  queue: MatchingQueueItemResponse[];
  runObservationAction: MatchingActionHandler;
  summary: MatchingSummaryResponse | null;
  setPage: (page: number) => void;
}

interface SongGroupingReviewState {
  activeSuggestionId: string | null;
  error: string | null;
  isGenerating: boolean;
  isLoading: boolean;
  loadSuggestions: () => Promise<void>;
  pageData: SongGroupingSuggestionPageResponse | null;
  reviewSuggestion: (suggestionId: string, accept: boolean) => Promise<void>;
  runGeneration: () => Promise<void>;
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

function useMatchingReviewQueue(): MatchingReviewState {
  const [summary, setSummary] = useState<MatchingSummaryResponse | null>(null);
  const [queue, setQueue] = useState<MatchingQueueItemResponse[]>([]);
  const [page, setPage] = useState(1);
  const [pageData, setPageData] = useState<MatchingQueuePageResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activeObservationId, setActiveObservationId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadQueue = useCallback(async () => {
    try {
      const [summaryResponse, queueResponse] = await Promise.all([
        matchingApi.getSummary(),
        matchingApi.getQueue(page),
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
  }, [page]);

  useEffect(() => {
    void loadQueue();
  }, [loadQueue]);

  const runObservationAction = useCallback(
    async (observationId: string, action: () => Promise<void>) => {
      const position = captureMatchingActionPosition();
      const observationIndex = queue.findIndex((item) => item.observationId === observationId);
      let fallbackObservationId: string | null = null;

      setActiveObservationId(observationId);
      try {
        await action();
        const queueResponse = await loadQueue();
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
    [loadQueue, queue],
  );

  return { activeObservationId, error, isLoading, loadQueue, page, pageData, queue, runObservationAction, setPage, summary };
}

function useSongGroupingReview(): SongGroupingReviewState {
  const [page, setPage] = useState(1);
  const [pageData, setPageData] = useState<SongGroupingSuggestionPageResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isGenerating, setIsGenerating] = useState(false);
  const [activeSuggestionId, setActiveSuggestionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadSuggestions = useCallback(async () => {
    try {
      const response = await matchingApi.getSongGroupingSuggestions(page);
      setPageData(response);
      if (response.page !== page) setPage(response.page);
      setError(null);
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load song grouping suggestions'));
    } finally {
      setIsLoading(false);
    }
  }, [page]);

  useEffect(() => {
    void loadSuggestions();
  }, [loadSuggestions]);

  const runGeneration = useCallback(async () => {
    setIsGenerating(true);
    try {
      await matchingApi.generateSongGroupingSuggestions();
      await loadSuggestions();
    } catch (generationError) {
      setError(getErrorMessage(generationError, 'Failed to find song grouping candidates'));
    } finally {
      setIsGenerating(false);
    }
  }, [loadSuggestions]);

  const reviewSuggestion = useCallback(async (suggestionId: string, accept: boolean) => {
    setActiveSuggestionId(suggestionId);
    try {
      await matchingApi.reviewSongGroupingSuggestion(suggestionId, accept);
      await loadSuggestions();
    } catch (reviewError) {
      setError(getErrorMessage(reviewError, 'Song grouping review failed'));
    } finally {
      setActiveSuggestionId(null);
    }
  }, [loadSuggestions]);

  return {
    activeSuggestionId,
    error,
    isGenerating,
    isLoading,
    loadSuggestions,
    pageData,
    reviewSuggestion,
    runGeneration,
    setPage,
  };
}

function formatDuration(durationSeconds?: number): string | null {
  if (!durationSeconds || durationSeconds <= 0) {
    return null;
  }

  const minutes = Math.floor(durationSeconds / 60);
  const seconds = durationSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

function formatStatus(status: string): string {
  switch (status) {
    case 'ambiguous':
      return 'Ambiguous';
    case 'no_match':
      return 'No match';
    case 'pending':
      return 'Pending';
    case 'matched':
      return 'Matched';
    default:
      return status;
  }
}

function formatPercent(value?: number): string | null {
  if (value === undefined || value === null) {
    return null;
  }

  return `${Math.round(value * 100)}%`;
}

function formatClusterReason(clusterReason?: string): string | null {
  if (!clusterReason) {
    return null;
  }

  switch (clusterReason) {
    case 'shared-strong-identifier':
      return 'Shared strong identifier';
    case 'normalized-title-artist-duration':
      return 'Merged by normalized title, artist, and duration';
    case 'representative':
      return 'Standalone cluster';
    default:
      return clusterReason;
  }
}

function MarkerList({
  markers,
  tone,
}: {
  markers: string[];
  tone: 'version' | 'playback';
}) {
  if (markers.length === 0) {
    return null;
  }

  const classes = tone === 'version'
    ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
    : 'border-rose-200 bg-rose-50 text-rose-700';

  return (
    <div className="mt-2 flex flex-wrap gap-2">
      {markers.map((marker) => (
        <span key={`${tone}-${marker}`} className={`rounded-full border px-2 py-1 text-[11px] font-semibold ${classes}`}>
          {marker}
        </span>
      ))}
    </div>
  );
}

function statusClasses(status: string): string {
  switch (status) {
    case 'ambiguous':
      return 'bg-amber-100 text-amber-800';
    case 'no_match':
      return 'bg-rose-100 text-rose-800';
    case 'pending':
      return 'bg-indigo-100 text-indigo-800';
    default:
      return 'bg-emerald-100 text-emerald-800';
  }
}

function formatSource(sourceType: string, externalId: string): string | ReactNode {
  switch (sourceType) {
    case 'spotify':
      return (
        <a href={`https://open.spotify.com/track/${externalId}`} target="_blank" rel="noopener noreferrer" className="text-indigo-600 hover:underline">
          Spotify - {externalId}
        </a>
      );
    case 'apple_music':
      return (
        <a href={`https://music.apple.com/track/${externalId}`} target="_blank" rel="noopener noreferrer" className="text-red-500 hover:underline">
          Apple Music - {externalId}
        </a>
      );
    case 'youtube':
      return (
        <a href={`https://youtu.be/${externalId}`} target="_blank" rel="noopener noreferrer" className="text-red-600 hover:underline">
          YouTube - {externalId}
        </a>
      );
    default:
      return `${sourceType} / ${externalId}`;
  }
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
    <header className="flex flex-wrap items-center justify-between gap-3">
      <div>
        <p className="text-xs tracking-[0.32em] text-content-muted uppercase">Matching review</p>
        <h1 className="mt-1 text-3xl font-bold">Resolve track identity</h1>
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

function groupingReason(suggestion: SongGroupingSuggestionResponse): string {
  try {
    const evidence = JSON.parse(suggestion.evidenceJson) as { reason?: string };
    return evidence.reason ?? 'The recordings have matching composition evidence.';
  } catch {
    return 'The recordings have matching composition evidence.';
  }
}

function SongGroupingTrack({
  label,
  track,
}: {
  label: string;
  track: SongGroupingTrackResponse;
}) {
  return (
    <div className="min-w-0 flex-1 px-4 py-3">
      <p className="text-xs font-black text-accent-strong">{label}</p>
      <h3 className="mt-1 truncate text-base font-black text-content">
        {track.title ?? 'Untitled recording'}
      </h3>
      <p className="mt-1 truncate text-sm font-semibold text-content-muted">
        {track.artist ?? 'Unknown artist'}
      </p>
      <p className="mt-2 font-mono text-xs text-content-muted">
        {track.isrc ? `ISRC ${track.isrc}` : track.musicBrainzRecordingId ? `MBID ${track.musicBrainzRecordingId}` : 'No stable recording ID'}
      </p>
    </div>
  );
}

function SongGroupingSuggestionRow({
  review,
  suggestion,
}: {
  review: SongGroupingReviewState;
  suggestion: SongGroupingSuggestionResponse;
}) {
  const busy = review.activeSuggestionId === suggestion.suggestionId;
  return (
    <article className="px-5 py-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-semibold text-content">{groupingReason(suggestion)}</p>
        <span className="rounded-full bg-accent-soft px-3 py-1 text-xs font-black text-accent-strong">
          {Math.round(suggestion.confidence * 100)}% confidence
        </span>
      </div>
      <div className="mt-3 flex flex-col rounded-xl bg-accent-soft sm:flex-row sm:divide-x sm:divide-border-subtle">
        <SongGroupingTrack label="Move this recording" track={suggestion.candidate} />
        <SongGroupingTrack label="Into this song" track={suggestion.anchor} />
      </div>
      <div className="mt-3 flex flex-wrap justify-end gap-2">
        <button
          type="button"
          disabled={busy}
          onClick={() => void review.reviewSuggestion(suggestion.suggestionId, false)}
          className="rounded-xl px-4 py-2 text-sm font-black text-content transition hover:bg-surface-subtle focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none disabled:opacity-50"
        >
          Keep separate
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={() => void review.reviewSuggestion(suggestion.suggestionId, true)}
          className="rounded-xl bg-action px-4 py-2 text-sm font-black text-action-content transition hover:bg-action-hover focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:opacity-50"
        >
          {busy ? 'Applying…' : 'Group versions'}
        </button>
      </div>
    </article>
  );
}

function SongGroupingReviewBody({ review }: { review: SongGroupingReviewState }) {
  const items = review.pageData?.items ?? [];
  if (review.isLoading) {
    return <p className="px-5 py-6 text-sm font-semibold text-content-muted">Loading song grouping review…</p>;
  }
  if (items.length === 0) {
    return (
      <div className="px-5 py-6">
        <p className="font-black text-content">No grouping decisions waiting</p>
        <p className="mt-1 text-sm font-semibold text-content-muted">
          Run the candidate finder after importing new playlists or resolving Track matches.
        </p>
      </div>
    );
  }
  return (
    <div className="divide-y divide-border-subtle">
      {items.map((suggestion) => (
        <SongGroupingSuggestionRow
          key={suggestion.suggestionId}
          review={review}
          suggestion={suggestion}
        />
      ))}
    </div>
  );
}

function SongGroupingReviewPanel({ review }: { review: SongGroupingReviewState }) {
  return (
    <section aria-labelledby="song-grouping-heading">
      <GlassCard className="overflow-hidden p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border-subtle px-5 py-4">
          <div>
            <h2 id="song-grouping-heading" className="text-xl font-black text-content">
              Group recordings into songs
            </h2>
            <p className="mt-1 max-w-2xl text-sm font-semibold text-content-muted">
              Review distinct recordings that may be versions of the same composition. Identical recording IDs stay in the separate Track-reconciliation workflow.
            </p>
          </div>
          <button
            type="button"
            onClick={() => void review.runGeneration()}
            disabled={review.isGenerating}
            className="rounded-xl bg-action px-4 py-2.5 text-sm font-black text-action-content transition hover:bg-action-hover focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50"
          >
            {review.isGenerating ? 'Finding candidates…' : 'Find candidates'}
          </button>
        </div>

        {review.error ? (
          <p className="bg-rose-50 px-5 py-3 text-sm font-semibold text-rose-800" role="alert">
            {review.error}
          </p>
        ) : null}

        <SongGroupingReviewBody review={review} />
      </GlassCard>
      <MatchingQueuePagination
        pageData={review.pageData}
        onPageChange={review.setPage}
        label="Song grouping review"
      />
    </section>
  );
}

function getMatchingSummaryStats(summary: MatchingSummaryResponse) {
  return [
    { label: 'Unresolved', value: summary.totalUnresolved, tint: 'from-indigo-500 to-purple-500' },
    { label: 'Pending', value: summary.pending, tint: 'from-sky-500 to-indigo-500' },
    { label: 'Ambiguous', value: summary.ambiguous, tint: 'from-amber-500 to-orange-500' },
    { label: 'No match', value: summary.noMatch, tint: 'from-rose-500 to-pink-500' },
  ];
}

function MatchingSummaryStats({ summary }: { summary: MatchingSummaryResponse | null }) {
  if (!summary) {
    return null;
  }

  return (
    <section className="grid gap-4 md:grid-cols-4">
      {getMatchingSummaryStats(summary).map((stat) => (
        <GlassCard key={stat.label} className="p-5">
          <p className="text-xs tracking-[0.2em] text-content-muted uppercase">{stat.label}</p>
          <p className={`mt-3 bg-linear-to-r ${stat.tint} bg-clip-text text-3xl font-bold text-transparent`}>
            {stat.value}
          </p>
        </GlassCard>
      ))}
    </section>
  );
}

function MatchingReviewError({ error }: { error: string | null }) {
  if (!error) {
    return null;
  }

  return (
    <GlassCard className="border-rose-300 bg-rose-50 p-4 text-sm text-rose-700">
      {error}
    </GlassCard>
  );
}

function MatchingQueueEmptyState() {
  return (
    <GlassCard className="p-8">
      <h2 className="text-2xl font-semibold">Queue is clear</h2>
      <p className="mt-2 text-sm text-content-muted">
        Imported songs are either matched already or there are no playlists waiting for review.
      </p>
    </GlassCard>
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
  return <nav className="mt-3 flex flex-wrap items-center justify-between gap-3 rounded-xl bg-surface-translucent px-4 py-3" aria-label={`${label} pages`}><p className="text-sm font-medium text-content-muted">Reviewing {firstItem}–{lastItem} of {pageData.totalCount}</p><div className="flex items-center gap-2"><button type="button" disabled={pageData.page <= 1} onClick={() => onPageChange(pageData.page - 1)} className="rounded-lg border border-border-subtle bg-surface px-3 py-2 text-sm font-semibold text-accent-strong transition hover:bg-accent-soft disabled:cursor-not-allowed disabled:opacity-40">Previous</button><span className="min-w-20 text-center text-sm text-content-muted">Page {pageData.page} of {pageData.totalPages}</span><button type="button" disabled={pageData.page >= pageData.totalPages} onClick={() => onPageChange(pageData.page + 1)} className="rounded-lg bg-action px-3 py-2 text-sm font-semibold text-action-content transition hover:bg-action-hover disabled:cursor-not-allowed disabled:opacity-40">Next</button></div></nav>;
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
    let nextIndex: number | null = null;

    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      nextIndex = (currentIndex + 1) % matchingReviewTabs.length;
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      nextIndex = (currentIndex - 1 + matchingReviewTabs.length) % matchingReviewTabs.length;
    } else if (event.key === 'Home') {
      nextIndex = 0;
    } else if (event.key === 'End') {
      nextIndex = matchingReviewTabs.length - 1;
    }

    if (nextIndex !== null) {
      event.preventDefault();
      selectTab(matchingReviewTabs[nextIndex].id, true);
    }
  };

  return (
    <div className="overflow-x-auto pb-1">
      <div
        role="tablist"
        aria-label="Matching review type"
        className="inline-flex min-w-full rounded-2xl border border-border-subtle bg-surface-translucent p-1 sm:min-w-0"
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
              className={`min-h-11 flex-1 rounded-xl px-4 py-2.5 text-sm font-black whitespace-nowrap transition focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none ${
                isActive
                  ? 'bg-action text-action-content shadow-sm'
                  : 'text-content hover:bg-accent-soft hover:text-accent-strong'
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
          <MatchingReviewError error={review.error} />
          <MatchingQueuePagination pageData={review.pageData} onPageChange={review.setPage} label="MusicBrainz matches" />
          <MatchingQueueList
            activeObservationId={review.activeObservationId}
            onAction={review.runObservationAction}
            queue={review.queue}
          />
          <MatchingQueuePagination pageData={review.pageData} onPageChange={review.setPage} label="MusicBrainz matches" />
        </section>
      ) : (
        <section id="track-groupings-panel" role="tabpanel" aria-labelledby="track-groupings-tab">
          <SongGroupingReviewPanel review={groupingReview} />
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
    return <img src={item.thumbnailUrl} alt="" className="h-28 w-28 shrink-0 rounded-2xl object-cover" />;
  }

  return (
    <div className="flex h-28 w-28 shrink-0 items-center justify-center rounded-2xl bg-surface-translucent text-sm text-content-muted">
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
    <div className="rounded-2xl border border-border-subtle bg-surface-translucent px-3 py-2">
      <p className="text-[11px] tracking-[0.2em] text-content-muted uppercase">{label}</p>
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
        <span className={`rounded-full px-3 py-1 text-xs font-semibold ${statusClasses(item.matchStatus)}`}>
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
        className="flex-1 rounded-2xl bg-surface px-5 py-3 text-sm font-semibold text-content transition hover:bg-surface-subtle disabled:cursor-not-allowed disabled:opacity-60 lg:flex-none"
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
    <div className="matching-playlist-appearances">
      <p>Appears in</p>
      <ul>
        {playlists.map((playlist) => (
          <li key={`${playlist.playlistId}-${playlist.position}`}>
            <span>{playlist.playlistName}</span>
            <span>#{playlist.position + 1}</span>
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
    <div className="rounded-2xl bg-surface-translucent p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs tracking-[0.2em] text-content-muted uppercase">Suggested candidates</p>
        <span className="rounded-full bg-surface px-2 py-1 text-[11px] font-semibold text-content-muted">
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

interface TrackVersionOption {
  label: string;
  value: number;
}

interface SuggestedTrackVersion {
  label: string;
  value: number;
}

const trackVersionOptions: readonly TrackVersionOption[] = [
  { label: 'Acoustic', value: 1 },
  { label: 'Live', value: 2 },
  { label: 'Instrumental', value: 4 },
  { label: 'Orchestral', value: 8 },
  { label: 'Remix', value: 16 },
  { label: 'Radio edit', value: 32 },
  { label: 'Extended', value: 64 },
  { label: 'Demo', value: 128 },
  { label: 'A cappella', value: 256 },
  { label: 'Karaoke', value: 512 },
  { label: 'Cover', value: 1024 },
  { label: 'Remastered', value: 2048 },
  { label: 'Re-recorded', value: 4096 },
  { label: 'Clean', value: 8192 },
  { label: 'Explicit', value: 16384 },
  { label: 'Slowed', value: 32768 },
  { label: 'Sped up', value: 65536 },
  { label: 'Alternate take', value: 131072 },
];

function getSuggestedTrackVersion(versionFlags: number): SuggestedTrackVersion | undefined {
  if (versionFlags === 0) {
    return undefined;
  }

  const labels = trackVersionOptions
    .filter((option) => (versionFlags & option.value) === option.value)
    .map((option) => option.label);

  return {
    label: labels.length > 0 ? labels.join(' + ') : 'Inferred',
    value: versionFlags,
  };
}

function CandidateList({
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
  if (candidates.length === 0) {
    return <p className="mt-3 text-sm text-content-muted">No candidates were stored for this observation yet.</p>;
  }

  const candidateRows: MatchingQueueCandidateResponse[][] = [];
  const detectedVersion = getSuggestedTrackVersion(suggestedVersionFlags);
  for (let index = 0; index < candidates.length; index += 2) {
    candidateRows.push(candidates.slice(index, index + 2));
  }

  return (
    <div className="matching-candidate-grid mt-4">
      {candidateRows.map((row) => (
        <div key={row.map((candidate) => candidate.candidateId).join('-')} className="matching-candidate-row">
          <CandidateCard
            candidate={row[0]}
            detectedVersion={detectedVersion}
            disabled={disabled}
            onUseExact={() => void onAction(observationId, () => matchingApi.selectCandidate(observationId, row[0].candidateId))}
            onUseAsVersion={(versionFlags) => void onAction(
              observationId,
              () => matchingApi.selectCandidateAsVersion(observationId, row[0].candidateId, versionFlags),
            )}
          />
          {row[1] ? (
            <>
              <div className="matching-candidate-column-divider" aria-hidden="true" />
              <CandidateCard
                candidate={row[1]}
                detectedVersion={detectedVersion}
                disabled={disabled}
                onUseExact={() => void onAction(observationId, () => matchingApi.selectCandidate(observationId, row[1].candidateId))}
                onUseAsVersion={(versionFlags) => void onAction(
                  observationId,
                  () => matchingApi.selectCandidateAsVersion(observationId, row[1].candidateId, versionFlags),
                )}
              />
            </>
          ) : null}
        </div>
      ))}
    </div>
  );
}

function QueueObservationItem({ item, activeObservationId, onAction }: QueueObservationItemProps) {
  const isBusy = activeObservationId === item.observationId;

  return (
    <GlassCard className="p-5" data-matching-observation-id={item.observationId}>
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

type ScoreTone = 'match' | 'close' | 'miss' | 'neutral';

function scoreTone(value?: number): ScoreTone {
  if (value === undefined || value === null) {
    return 'neutral';
  }

  if (value >= 0.9) {
    return 'match';
  }

  if (value >= 0.65) {
    return 'close';
  }

  return 'miss';
}

function scoreToneClasses(tone: ScoreTone): string {
  switch (tone) {
    case 'match':
      return 'score-tone--match';
    case 'close':
      return 'score-tone--close';
    case 'miss':
      return 'score-tone--miss';
    default:
      return 'score-tone--neutral';
  }
}

function CandidateConfidence({ score }: { score: number }) {
  return (
    <span className={`matching-candidate-confidence ${scoreToneClasses(scoreTone(score))}`}>
      {Math.round(score * 100)}%
    </span>
  );
}

function formatCandidateSource(source?: string): string {
  if (!source) {
    return 'Unknown';
  }

  switch (source) {
    case 'musicbrainz':
      return 'MusicBrainz';
    default:
      return source.replace(/_/g, ' ');
  }
}

function CandidateMeta({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  const duration = formatDuration(candidate.durationSeconds);
  const sourceLabel = formatCandidateSource(candidate.candidateSource);
  const source = candidate.mbidRecording && candidate.candidateSource === 'musicbrainz'
    ? (
        <a
          href={`https://musicbrainz.org/recording/${candidate.mbidRecording}`}
          target="_blank"
          rel="noreferrer"
          className="matching-candidate-source-link"
        >
          {sourceLabel}
        </a>
      )
    : sourceLabel;

  return (
    <p className="matching-candidate-meta">
      <span>{candidate.artist ?? 'Unknown artist'}</span>
      {duration ? <span>{duration}</span> : null}
      <span>{source}</span>
    </p>
  );
}

function CandidateMarkers({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  return (
    <>
      <MarkerList markers={candidate.versionMarkers} tone="version" />
      <MarkerList markers={candidate.playbackModifiers} tone="playback" />
    </>
  );
}

function EvidenceRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="matching-evidence-row">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

interface CandidateEvidenceItem {
  label: string;
  value: ReactNode;
}

function hasEvidenceItem(item: CandidateEvidenceItem | null): item is CandidateEvidenceItem {
  return item !== null;
}

function getClusterEvidence(candidate: MatchingQueueCandidateResponse): CandidateEvidenceItem | null {
  const clusterReason = formatClusterReason(candidate.clusterReason);
  const shouldShowCluster = clusterReason && (candidate.clusterSize > 1 || candidate.clusterReason !== 'representative');

  return shouldShowCluster ? { label: 'Cluster', value: `${clusterReason} (${candidate.clusterSize})` } : null;
}

function getCandidateEvidenceItems(candidate: MatchingQueueCandidateResponse): CandidateEvidenceItem[] {
  return [
    getClusterEvidence(candidate),
  ].filter(hasEvidenceItem);
}

function CandidateEvidenceTable({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  const evidenceItems = getCandidateEvidenceItems(candidate);

  if (evidenceItems.length === 0) {
    return null;
  }

  return (
    <dl className="matching-evidence-table">
      {evidenceItems.map((item) => (
        <EvidenceRow key={item.label} label={item.label} value={item.value} />
      ))}
    </dl>
  );
}

function formatComparisonScore(comparison: MatchingCandidateComparisonResponse): string {
  if (comparison.scoreLabel) {
    return comparison.scoreLabel;
  }

  return comparison.score === undefined || comparison.score === null
    ? '-'
    : formatPercent(comparison.score) ?? '-';
}

function CandidateDiffRow({ comparison }: { comparison: MatchingCandidateComparisonResponse }) {
  return (
    <div className="matching-diff-row" role="row">
      <span className="matching-diff-label" role="cell">{comparison.label}</span>
      <span role="cell">{comparison.observationValue ?? 'None'}</span>
      <span role="cell">{comparison.candidateValue ?? 'None'}</span>
      <span role="cell">
        <span className={`matching-diff-score ${scoreToneClasses(comparison.tone)}`}>
          {formatComparisonScore(comparison)}
        </span>
      </span>
    </div>
  );
}

function CandidateDiffTable({ comparisons }: { comparisons: MatchingCandidateComparisonResponse[] }) {
  if (comparisons.length === 0) {
    return null;
  }

  const hiddenMatches = comparisons.filter((comparison) => comparison.scoreLabel === 'Match');
  const visibleComparisons = comparisons.filter((comparison) => comparison.scoreLabel !== 'Match');

  return (
    <div className="matching-diff-table" role="table" aria-label="Candidate differences">
      <div className="matching-diff-header" role="row">
        <span role="columnheader">Field</span>
        <span role="columnheader">Observation</span>
        <span role="columnheader">Candidate</span>
        <span role="columnheader">Match</span>
      </div>
      {visibleComparisons.map((comparison) => (
        <CandidateDiffRow key={comparison.label} comparison={comparison} />
      ))}
      {hiddenMatches.length > 0 ? (
        <details className="matching-diff-matches">
          <summary>{hiddenMatches.length} matching {hiddenMatches.length === 1 ? 'field' : 'fields'} hidden</summary>
          <div className="matching-diff-matches-body">
            {hiddenMatches.map((comparison) => (
              <CandidateDiffRow key={comparison.label} comparison={comparison} />
            ))}
          </div>
        </details>
      ) : null}
    </div>
  );
}

interface CandidateSelectionActionProps {
  candidateId: string;
  detectedVersion?: SuggestedTrackVersion;
  disabled: boolean;
  onUseAsVersion: (versionFlags: number) => void;
  onUseExact: () => void;
}

function CandidatePrimaryAction({
  detectedVersion,
  disabled,
  onUseAsVersion,
  onUseExact,
}: Omit<CandidateSelectionActionProps, 'candidateId'>) {
  if (!detectedVersion) {
    return (
      <button
        type="button"
        className="matching-candidate-action w-full focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none"
        onClick={onUseExact}
        disabled={disabled}
      >
        Use match
      </button>
    );
  }

  return (
    <button
      type="button"
      className="matching-candidate-action w-full focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none"
      onClick={() => onUseAsVersion(detectedVersion.value)}
      disabled={disabled}
    >
      Add as {detectedVersion.label.toLowerCase()} version
    </button>
  );
}

function CandidateAdditionalOptions({
  candidateId,
  detectedVersion,
  disabled,
  onUseAsVersion,
  onUseExact,
}: CandidateSelectionActionProps) {
  const [selectedVersionFlag, setSelectedVersionFlag] = useState(
    trackVersionOptions.some((option) => option.value === detectedVersion?.value)
      ? detectedVersion?.value ?? 0
      : 0,
  );
  const selectedVersion = trackVersionOptions.find((option) => option.value === selectedVersionFlag);
  const selectId = `candidate-version-${candidateId}`;

  return (
    <details className="rounded-xl border border-border-subtle bg-surface-translucent p-2">
      <summary className="cursor-pointer list-none rounded-lg px-2 py-1.5 text-sm font-semibold text-content transition hover:bg-accent-soft focus-visible:ring-2 focus-visible:ring-focus focus-visible:outline-none [&::-webkit-details-marker]:hidden">
        Additional options
      </summary>
      <div className="mt-2 space-y-2 px-2 pb-1">
        <label htmlFor={selectId} className="block text-xs font-semibold text-content-muted">Version type</label>
        <select
          id={selectId}
          value={selectedVersionFlag}
          onChange={(event) => setSelectedVersionFlag(Number(event.target.value))}
          disabled={disabled}
          className="min-h-10 w-full rounded-lg border border-border-subtle bg-surface px-3 text-sm font-semibold text-content focus:border-accent focus:ring-2 focus:ring-focus focus:outline-none disabled:cursor-not-allowed disabled:opacity-50"
        >
          <option value={0}>Choose a version type</option>
          {trackVersionOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>
        <button
          type="button"
          className="w-full rounded-lg border border-border-subtle bg-accent-soft px-3 py-2 text-sm font-semibold text-accent-strong transition hover:bg-accent-soft focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50"
          onClick={() => onUseAsVersion(selectedVersionFlag)}
          disabled={disabled || !selectedVersion}
        >
          {selectedVersion ? `Add as ${selectedVersion.label.toLowerCase()} version` : 'Choose a version type'}
        </button>
        <button type="button" className="w-full rounded-lg px-3 py-2 text-sm font-semibold text-content transition hover:bg-surface-subtle focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50" onClick={onUseExact} disabled={disabled}>
          Use as exact recording
        </button>
      </div>
    </details>
  );
}

function CandidateSelectionActions(props: CandidateSelectionActionProps) {
  return (
    <div className="flex w-full shrink-0 flex-col gap-2 sm:w-56">
      <CandidatePrimaryAction {...props} />
      <CandidateAdditionalOptions {...props} />
    </div>
  );
}

function CandidateCard({
  candidate,
  detectedVersion,
  disabled,
  onUseAsVersion,
  onUseExact,
}: {
  candidate: MatchingQueueCandidateResponse;
  detectedVersion?: SuggestedTrackVersion;
  disabled: boolean;
  onUseAsVersion: (versionFlags: number) => void;
  onUseExact: () => void;
}) {
  return (
    <div className="matching-candidate-card">
      <div className="matching-candidate-shell">
        <div className="matching-candidate-header">
          <div className="min-w-0">
            <div className="matching-candidate-title-row">
              <h3>{candidate.title}</h3>
              <CandidateConfidence score={candidate.score} />
            </div>
            <CandidateMeta candidate={candidate} />
          </div>

          <CandidateSelectionActions
            candidateId={candidate.candidateId}
            detectedVersion={detectedVersion}
            disabled={disabled}
            onUseAsVersion={onUseAsVersion}
            onUseExact={onUseExact}
          />
        </div>

        <CandidateMarkers candidate={candidate} />
        <CandidateDiffTable comparisons={candidate.comparisons ?? []} />
        <CandidateEvidenceTable candidate={candidate} />
      </div>
    </div>
  );
}
