import { useCallback, useEffect, useState } from 'react';
import { GlassCard, GradientButton, GradientPageShell, PageLoadingState } from '@cantaro/client-shared/ui';
import { matchingApi } from '@cantaro/client-shared/music';
import type { ReactNode } from 'react';
import type {
  MatchingCandidateComparisonResponse,
  MatchingQueueCandidateResponse,
  MatchingQueueItemResponse,
  MatchingQueuePageResponse,
  MatchingQueuePlaylistResponse,
  MatchingSummaryResponse,
} from '@cantaro/client-shared/music';

interface MatchingReviewPageProps {
  embedded?: boolean;
}

type MatchingActionHandler = (observationId: string, action: () => Promise<void>) => Promise<void>;

interface MatchingReviewState {
  activeObservationId: string | null;
  error: string | null;
  isLoading: boolean;
  loadQueue: () => Promise<void>;
  page: number;
  pageData: MatchingQueuePageResponse | null;
  queue: MatchingQueueItemResponse[];
  runObservationAction: MatchingActionHandler;
  summary: MatchingSummaryResponse | null;
  setPage: (page: number) => void;
}

function getErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
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
    setIsLoading(true);
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
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load matching queue'));
    } finally {
      setIsLoading(false);
    }
  }, [page]);

  useEffect(() => {
    void loadQueue();
  }, [loadQueue]);

  const runObservationAction = useCallback(
    async (observationId: string, action: () => Promise<void>) => {
      setActiveObservationId(observationId);
      try {
        await action();
        await loadQueue();
      } catch (actionError) {
        setError(getErrorMessage(actionError, 'Matching action failed'));
      } finally {
        setActiveObservationId(null);
      }
    },
    [loadQueue],
  );

  return { activeObservationId, error, isLoading, loadQueue, page, pageData, queue, runObservationAction, setPage, summary };
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
      <p className="text-sm text-gray-600">Loading matching review queue...</p>
    </GlassCard>
  ) : (
    <PageLoadingState message="Loading matching review queue..." />
  );
}

function MatchingReviewLayout({ children, embedded }: { children: ReactNode; embedded: boolean }) {
  return embedded ? (
    <div className="space-y-5">{children}</div>
  ) : (
    <GradientPageShell className="text-gray-900">{children}</GradientPageShell>
  );
}

function MatchingReviewHeader({ onRefresh }: { onRefresh: () => void }) {
  return (
    <header className="flex flex-wrap items-center justify-between gap-3">
      <div>
        <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Matching review</p>
        <h1 className="mt-1 text-3xl font-bold">Resolve track identity</h1>
        <p className="mt-2 text-sm text-gray-600">
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
          <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">{stat.label}</p>
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
      <p className="mt-2 text-sm text-gray-600">
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

function MatchingQueuePagination({ pageData, onPageChange }: { pageData: MatchingQueuePageResponse | null; onPageChange: (page: number) => void }) {
  if (!pageData || pageData.totalPages <= 1) return null;
  const firstItem = (pageData.page - 1) * pageData.pageSize + 1;
  const lastItem = Math.min(pageData.page * pageData.pageSize, pageData.totalCount);
  return <nav className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-white/70 px-4 py-3" aria-label="Matching queue pages"><p className="text-sm font-medium text-gray-600">Reviewing {firstItem}–{lastItem} of {pageData.totalCount}</p><div className="flex items-center gap-2"><button type="button" disabled={pageData.page <= 1} onClick={() => onPageChange(pageData.page - 1)} className="rounded-lg border border-violet-200 bg-white px-3 py-2 text-sm font-semibold text-violet-800 transition hover:bg-violet-50 disabled:cursor-not-allowed disabled:opacity-40">Previous</button><span className="min-w-20 text-center text-sm text-gray-600">Page {pageData.page} of {pageData.totalPages}</span><button type="button" disabled={pageData.page >= pageData.totalPages} onClick={() => onPageChange(pageData.page + 1)} className="rounded-lg bg-violet-700 px-3 py-2 text-sm font-semibold text-white transition hover:bg-violet-800 disabled:cursor-not-allowed disabled:opacity-40">Next</button></div></nav>;
}

function MatchingReviewContent({ review }: { review: MatchingReviewState }) {
  return (
    <>
      <MatchingReviewHeader onRefresh={() => void review.loadQueue()} />
      <MatchingSummaryStats summary={review.summary} />
      <MatchingReviewError error={review.error} />
      <MatchingQueuePagination pageData={review.pageData} onPageChange={review.setPage} />
      <MatchingQueueList
        activeObservationId={review.activeObservationId}
        onAction={review.runObservationAction}
        queue={review.queue}
      />
      <MatchingQueuePagination pageData={review.pageData} onPageChange={review.setPage} />
    </>
  );
}

export function MatchingReviewPage({ embedded = false }: MatchingReviewPageProps) {
  const review = useMatchingReviewQueue();

  if (review.isLoading) {
    return <MatchingReviewLoading embedded={embedded} />;
  }

  return (
    <MatchingReviewLayout embedded={embedded}>
      <MatchingReviewContent review={review} />
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
    <div className="flex h-28 w-28 shrink-0 items-center justify-center rounded-2xl bg-white/80 text-sm text-gray-500">
      No art
    </div>
  );
}

function ObservationAttemptMeta({ item }: { item: MatchingQueueItemResponse }) {
  const lastAttempt = item.lastMatchAttemptedAt
    ? ` - Last tried ${new Date(item.lastMatchAttemptedAt).toLocaleString()}`
    : '';

  return (
    <p className="mt-2 text-xs text-gray-500">
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
    <div className="rounded-2xl border border-white/70 bg-white/75 px-3 py-2">
      <p className="text-[11px] tracking-[0.2em] text-gray-500 uppercase">{label}</p>
      <p className="mt-1 text-lg font-semibold text-gray-900">{value}</p>
    </div>
  );
}

function ObservationSummary({ item }: { item: MatchingQueueItemResponse }) {
  const duration = formatDuration(item.durationSeconds);

  return (
    <div className="min-w-0 flex-1">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="text-xl font-semibold text-gray-900">{item.title}</h2>
        <span className={`rounded-full px-3 py-1 text-xs font-semibold ${statusClasses(item.matchStatus)}`}>
          {formatStatus(item.matchStatus)}
        </span>
      </div>
      <p className="mt-2 text-sm text-gray-600">
        {item.artist ?? 'Unknown artist'} {duration ? `- ${duration}` : ''}
      </p>
      <p className="mt-2 text-xs text-gray-500">Source: {formatSource(item.sourceType, item.externalId)}</p>
      <p className="mt-2 text-sm text-gray-700">{item.resolutionNotes ?? 'Waiting for a matching decision.'}</p>
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
        className="flex-1 rounded-2xl bg-white px-5 py-3 text-sm font-semibold text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-60 lg:flex-none"
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
}: {
  candidates: MatchingQueueCandidateResponse[];
  disabled: boolean;
  observationId: string;
  onAction: MatchingActionHandler;
}) {
  return (
    <div className="rounded-2xl bg-white/70 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">Suggested candidates</p>
        <span className="rounded-full bg-white px-2 py-1 text-[11px] font-semibold text-gray-500">
          {candidates.length} stored
        </span>
      </div>
      <CandidateList
        candidates={candidates}
        disabled={disabled}
        observationId={observationId}
        onAction={onAction}
      />
    </div>
  );
}

function CandidateList({
  candidates,
  disabled,
  observationId,
  onAction,
}: {
  candidates: MatchingQueueCandidateResponse[];
  disabled: boolean;
  observationId: string;
  onAction: MatchingActionHandler;
}) {
  if (candidates.length === 0) {
    return <p className="mt-3 text-sm text-gray-600">No candidates were stored for this observation yet.</p>;
  }

  const candidateRows: MatchingQueueCandidateResponse[][] = [];
  for (let index = 0; index < candidates.length; index += 2) {
    candidateRows.push(candidates.slice(index, index + 2));
  }

  return (
    <div className="matching-candidate-grid mt-4">
      {candidateRows.map((row) => (
        <div key={row.map((candidate) => candidate.candidateId).join('-')} className="matching-candidate-row">
          <CandidateCard
            candidate={row[0]}
            disabled={disabled}
            onUse={() => void onAction(observationId, () => matchingApi.selectCandidate(observationId, row[0].candidateId))}
          />
          {row[1] ? (
            <>
              <div className="matching-candidate-column-divider" aria-hidden="true" />
              <CandidateCard
                candidate={row[1]}
                disabled={disabled}
                onUse={() => void onAction(observationId, () => matchingApi.selectCandidate(observationId, row[1].candidateId))}
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
    <GlassCard className="p-5">
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

function CandidateCard({
  candidate,
  disabled,
  onUse,
}: {
  candidate: MatchingQueueCandidateResponse;
  disabled: boolean;
  onUse: () => void;
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

          <button
            type="button"
            className="matching-candidate-action"
            onClick={onUse}
            disabled={disabled}
          >
            Use match
          </button>
        </div>

        <CandidateMarkers candidate={candidate} />
        <CandidateDiffTable comparisons={candidate.comparisons ?? []} />
        <CandidateEvidenceTable candidate={candidate} />
      </div>
    </div>
  );
}
