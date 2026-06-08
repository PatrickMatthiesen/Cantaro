import { useCallback, useEffect, useState } from 'react';
import { GlassCard, GradientButton, GradientPageShell, PageLoadingState } from '@cantaro/client-shared/ui';
import { matchingApi } from '@cantaro/client-shared/music';
import type { ReactNode } from 'react';
import type {
  MatchingQueueCandidateResponse,
  MatchingQueueItemResponse,
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
  queue: MatchingQueueItemResponse[];
  runObservationAction: MatchingActionHandler;
  summary: MatchingSummaryResponse | null;
}

function getErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

function useMatchingReviewQueue(): MatchingReviewState {
  const [summary, setSummary] = useState<MatchingSummaryResponse | null>(null);
  const [queue, setQueue] = useState<MatchingQueueItemResponse[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [activeObservationId, setActiveObservationId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadQueue = useCallback(async () => {
    setIsLoading(true);
    try {
      const [summaryResponse, queueResponse] = await Promise.all([
        matchingApi.getSummary(),
        matchingApi.getQueue(),
      ]);
      setSummary(summaryResponse);
      setQueue(queueResponse);
      setError(null);
    } catch (loadError) {
      setError(getErrorMessage(loadError, 'Failed to load matching queue'));
    } finally {
      setIsLoading(false);
    }
  }, []);

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

  return { activeObservationId, error, isLoading, loadQueue, queue, runObservationAction, summary };
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

function MatchingReviewContent({ review }: { review: MatchingReviewState }) {
  return (
    <>
      <MatchingReviewHeader onRefresh={() => void review.loadQueue()} />
      <MatchingSummaryStats summary={review.summary} />
      <MatchingReviewError error={review.error} />
      <MatchingQueueList
        activeObservationId={review.activeObservationId}
        onAction={review.runObservationAction}
        queue={review.queue}
      />
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
    <div className="rounded-2xl bg-white/70 p-4">
      <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">Appears in playlists</p>
      <ul className="mt-3 space-y-2 text-sm text-gray-700">
        {playlists.map((playlist) => (
          <li key={`${playlist.playlistId}-${playlist.position}`} className="rounded-xl bg-white px-3 py-2">
            {playlist.playlistName} <span className="text-gray-500">- position {playlist.position + 1}</span>
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

  return (
    <div className="mt-3 space-y-3">
      {candidates.map((candidate) => (
        <CandidateCard
          key={candidate.candidateId}
          candidate={candidate}
          disabled={disabled}
          onUse={() => void onAction(observationId, () => matchingApi.selectCandidate(observationId, candidate.candidateId))}
        />
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

      <div className="mt-4 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
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

// fallow-ignore-next-line complexity
function CandidateScoreBadges({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  const duration = formatDuration(candidate.durationSeconds);
  const semanticClass = candidate.semanticAdjustment && candidate.semanticAdjustment < 0
    ? 'bg-rose-100 text-rose-700'
    : 'bg-emerald-100 text-emerald-700';
  const semanticLabel = candidate.semanticAdjustment !== undefined
    ? `${candidate.semanticAdjustment > 0 ? '+' : ''}${Math.round(candidate.semanticAdjustment * 100)} pts`
    : '—';

  return (
    <>
      <p className="mt-1 text-xs text-gray-500">
        {candidate.artist ?? 'Unknown artist'} {duration ? `- ${duration}` : ''} - {candidate.candidateSource}
      </p>
      <MarkerList markers={candidate.versionMarkers} tone="version" />
      <MarkerList markers={candidate.playbackModifiers} tone="playback" />
      <div className="mt-3 flex flex-wrap gap-2 text-[11px] text-gray-600">
        <span className="rounded-full bg-slate-100 px-2 py-1 font-semibold text-slate-700">
          Title {formatPercent(candidate.titleSimilarity) ?? '—'}
        </span>
        <span className="rounded-full bg-slate-100 px-2 py-1 font-semibold text-slate-700">
          Artist {formatPercent(candidate.artistSimilarity) ?? '—'}
        </span>
        <span className="rounded-full bg-slate-100 px-2 py-1 font-semibold text-slate-700">
          Duration {formatPercent(candidate.durationScore) ?? '—'}
        </span>
        <span className={`rounded-full px-2 py-1 font-semibold ${semanticClass}`}>
          Semantic {semanticLabel}
        </span>
        <span className="rounded-full bg-indigo-50 px-2 py-1 font-semibold text-indigo-700">
          Cluster size {candidate.clusterSize}
        </span>
      </div>
    </>
  );
}

function CandidateNotes({ candidate }: { candidate: MatchingQueueCandidateResponse }) {
  const clusterReason = formatClusterReason(candidate.clusterReason);
  return (
    <>
      {clusterReason ? <p className="mt-2 text-xs font-medium text-gray-600">{clusterReason}</p> : null}
      {candidate.semanticExplanation ? <p className="mt-2 text-xs text-gray-600">{candidate.semanticExplanation}</p> : null}
      {candidate.mbidRecording ? (
        <p className="mt-1 text-xs text-gray-500">
          MBID:{' '}
          <a
            href={`https://musicbrainz.org/recording/${candidate.mbidRecording}`}
            target="_blank"
            rel="noreferrer"
            className="font-medium text-indigo-700 underline decoration-indigo-300 underline-offset-2 hover:text-indigo-800"
          >
            {candidate.mbidRecording}
          </a>
        </p>
      ) : null}
      {candidate.explanation ? <p className="mt-2 text-sm text-gray-700">{candidate.explanation}</p> : null}
    </>
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
    <div className="rounded-2xl bg-white px-4 py-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <p className="text-sm font-semibold text-gray-900">{candidate.title}</p>
            <span className="rounded-full bg-indigo-100 px-2 py-1 text-[11px] font-semibold text-indigo-800">
              {Math.round(candidate.score * 100)}%
            </span>
          </div>
          <CandidateScoreBadges candidate={candidate} />
          <CandidateNotes candidate={candidate} />
        </div>

        <button
          type="button"
          className="rounded-xl bg-linear-to-r from-indigo-500 to-purple-500 px-4 py-2 text-sm font-semibold text-white transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-60"
          onClick={onUse}
          disabled={disabled}
        >
          Use match
        </button>
      </div>
    </div>
  );
}
