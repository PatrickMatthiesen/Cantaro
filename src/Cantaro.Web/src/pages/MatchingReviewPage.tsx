import { useCallback, useEffect, useState } from 'react';
import { GlassCard, GradientButton } from '../components/ui/GlassComponents';
import { matchingApi } from '../services';
import type {
  MatchingQueueCandidateResponse,
  MatchingQueueItemResponse,
  MatchingSummaryResponse,
} from '../services';

interface MatchingReviewPageProps {
  onNavigateHome: () => void;
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

function formatSource(sourceType: string, externalId: string): string | React.ReactNode {
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

export function MatchingReviewPage({ onNavigateHome }: MatchingReviewPageProps) {
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
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load matching queue');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadQueue();
  }, [loadQueue]);

  const handleObservationAction = useCallback(
    async (observationId: string, action: () => Promise<void>) => {
      setActiveObservationId(observationId);
      try {
        await action();
        await loadQueue();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Matching action failed');
      } finally {
        setActiveObservationId(null);
      }
    },
    [loadQueue],
  );

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50">
        <GlassCard className="px-6 py-4">
          <p className="text-sm text-gray-700">Loading matching review queue…</p>
        </GlassCard>
      </div>
    );
  }

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto max-w-6xl space-y-5 px-6 pt-8 pb-16">
        <header className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Matching review</p>
            <h1 className="mt-1 text-3xl font-bold">Resolve track identity</h1>
            <p className="mt-2 text-sm text-gray-600">
              Review ambiguous and unmatched imports before they become canonical Cantaro tracks.
            </p>
          </div>
          <div className="flex gap-2">
            <GradientButton tone="soft" onClick={onNavigateHome}>
              Back to home
            </GradientButton>
            <GradientButton tone="soft" onClick={() => void loadQueue()}>
              Refresh queue
            </GradientButton>
          </div>
        </header>

        {summary ? (
          <section className="grid gap-4 md:grid-cols-4">
            {[
              { label: 'Unresolved', value: summary.totalUnresolved, tint: 'from-indigo-500 to-purple-500' },
              { label: 'Pending', value: summary.pending, tint: 'from-sky-500 to-indigo-500' },
              { label: 'Ambiguous', value: summary.ambiguous, tint: 'from-amber-500 to-orange-500' },
              { label: 'No match', value: summary.noMatch, tint: 'from-rose-500 to-pink-500' },
            ].map((stat) => (
              <GlassCard key={stat.label} className="p-5">
                <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">{stat.label}</p>
                <p className={`mt-3 bg-linear-to-r ${stat.tint} bg-clip-text text-3xl font-bold text-transparent`}>
                  {stat.value}
                </p>
              </GlassCard>
            ))}
          </section>
        ) : null}

        {error ? (
          <GlassCard className="border-rose-300 bg-rose-50 p-4 text-sm text-rose-700">
            {error}
          </GlassCard>
        ) : null}

        {queue.length === 0 ? (
          <GlassCard className="p-8">
            <h2 className="text-2xl font-semibold">Queue is clear</h2>
            <p className="mt-2 text-sm text-gray-600">
              Imported songs are either matched already or there are no playlists waiting for review.
            </p>
          </GlassCard>
        ) : (
          <section className="space-y-4">
            {queue.map((item) => {
              const duration = formatDuration(item.durationSeconds);
              const isBusy = activeObservationId === item.observationId;

              return (
                <GlassCard key={item.observationId} className="p-5">
                  <div className="flex flex-col gap-4 lg:flex-row">
                    <div className="flex min-w-0 flex-1 gap-4">
                      {item.thumbnailUrl ? (
                        <img src={item.thumbnailUrl} alt="" className="h-28 w-28 rounded-2xl object-cover" />
                      ) : (
                        <div className="flex h-28 w-28 items-center justify-center rounded-2xl bg-white/80 text-sm text-gray-500">
                          No art
                        </div>
                      )}

                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <h2 className="text-xl font-semibold text-gray-900">{item.title}</h2>
                          <span className={`rounded-full px-3 py-1 text-xs font-semibold ${statusClasses(item.matchStatus)}`}>
                            {formatStatus(item.matchStatus)}
                          </span>
                        </div>
                        <p className="mt-2 text-sm text-gray-600">
                          {item.artist ?? 'Unknown artist'} {duration ? `• ${duration}` : ''}
                        </p>
                        <p className="mt-2 text-xs text-gray-500">
                          Source: {formatSource(item.sourceType, item.externalId)}
                        </p>
                        <p className="mt-2 text-sm text-gray-700">
                          {item.resolutionNotes ?? 'Waiting for a matching decision.'}
                        </p>
                        <MarkerList markers={item.diagnostics.versionMarkers} tone="version" />
                        <MarkerList markers={item.diagnostics.playbackModifiers} tone="playback" />
                        {item.lastMatchError ? (
                          <p className="mt-2 text-sm text-rose-700">Last error: {item.lastMatchError}</p>
                        ) : null}
                        <p className="mt-2 text-xs text-gray-500">
                          Attempts: {item.matchAttemptCount}
                          {item.lastMatchAttemptedAt
                            ? ` • Last tried ${new Date(item.lastMatchAttemptedAt).toLocaleString()}`
                            : ''}
                        </p>
                        <div className="mt-3 grid gap-2 sm:grid-cols-3">
                          <div className="rounded-2xl border border-white/70 bg-white/75 px-3 py-2">
                            <p className="text-[11px] tracking-[0.2em] text-gray-500 uppercase">Top cluster</p>
                            <p className="mt-1 text-lg font-semibold text-gray-900">{formatPercent(item.diagnostics.topScore) ?? '—'}</p>
                          </div>
                          <div className="rounded-2xl border border-white/70 bg-white/75 px-3 py-2">
                            <p className="text-[11px] tracking-[0.2em] text-gray-500 uppercase">Runner-up</p>
                            <p className="mt-1 text-lg font-semibold text-gray-900">{formatPercent(item.diagnostics.secondDistinctScore) ?? '—'}</p>
                          </div>
                          <div className="rounded-2xl border border-white/70 bg-white/75 px-3 py-2">
                            <p className="text-[11px] tracking-[0.2em] text-gray-500 uppercase">Distinct clusters</p>
                            <p className="mt-1 text-lg font-semibold text-gray-900">{item.diagnostics.distinctClusterCount}</p>
                          </div>
                        </div>
                      </div>
                    </div>

                    <div className="flex flex-wrap gap-2 lg:w-64 lg:flex-col">
                      <GradientButton
                        onClick={() => void handleObservationAction(item.observationId, () => matchingApi.retry(item.observationId))}
                        disabled={isBusy}
                      >
                        {isBusy ? 'Working…' : 'Retry matching'}
                      </GradientButton>
                      <GradientButton
                        tone="soft"
                        onClick={() =>
                          void handleObservationAction(item.observationId, () => matchingApi.createTrack(item.observationId))
                        }
                        disabled={isBusy}
                      >
                        Create canonical track
                      </GradientButton>
                      <button
                        type="button"
                        className="rounded-xl bg-white px-4 py-2 text-sm font-semibold text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-60"
                        onClick={() =>
                          void handleObservationAction(item.observationId, () => matchingApi.markNoMatch(item.observationId))
                        }
                        disabled={isBusy}
                      >
                        Mark no match
                      </button>
                    </div>
                  </div>

                  <div className="mt-4 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
                    <div className="rounded-2xl bg-white/70 p-4">
                      <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">Appears in playlists</p>
                      <ul className="mt-3 space-y-2 text-sm text-gray-700">
                        {item.playlists.map((playlist) => (
                          <li key={`${playlist.playlistId}-${playlist.position}`} className="rounded-xl bg-white px-3 py-2">
                            {playlist.playlistName} <span className="text-gray-500">• position {playlist.position + 1}</span>
                          </li>
                        ))}
                      </ul>
                    </div>

                    <div className="rounded-2xl bg-white/70 p-4">
                      <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">Suggested candidates</p>
                      {item.candidates.length === 0 ? (
                        <p className="mt-3 text-sm text-gray-600">No candidates were stored for this observation yet.</p>
                      ) : (
                        <div className="mt-3 space-y-3">
                          {item.candidates.map((candidate) => (
                            <CandidateCard
                              key={candidate.candidateId}
                              candidate={candidate}
                              disabled={isBusy}
                              onUse={() =>
                                void handleObservationAction(item.observationId, () =>
                                  matchingApi.selectCandidate(item.observationId, candidate.candidateId),
                                )
                              }
                            />
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                </GlassCard>
              );
            })}
          </section>
        )}
      </div>
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
  const duration = formatDuration(candidate.durationSeconds);

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
          <p className="mt-1 text-xs text-gray-500">
            {candidate.artist ?? 'Unknown artist'} {duration ? `• ${duration}` : ''} • {candidate.candidateSource}
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
            <span className={`rounded-full px-2 py-1 font-semibold ${candidate.semanticAdjustment && candidate.semanticAdjustment < 0 ? 'bg-rose-100 text-rose-700' : 'bg-emerald-100 text-emerald-700'}`}>
              Semantic {candidate.semanticAdjustment !== undefined ? `${candidate.semanticAdjustment > 0 ? '+' : ''}${Math.round(candidate.semanticAdjustment * 100)} pts` : '—'}
            </span>
            <span className="rounded-full bg-indigo-50 px-2 py-1 font-semibold text-indigo-700">
              Cluster size {candidate.clusterSize}
            </span>
          </div>
          {formatClusterReason(candidate.clusterReason) ? (
            <p className="mt-2 text-xs font-medium text-gray-600">{formatClusterReason(candidate.clusterReason)}</p>
          ) : null}
          {candidate.semanticExplanation ? (
            <p className="mt-2 text-xs text-gray-600">{candidate.semanticExplanation}</p>
          ) : null}
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
          {candidate.explanation ? (
            <p className="mt-2 text-sm text-gray-700">{candidate.explanation}</p>
          ) : null}
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
