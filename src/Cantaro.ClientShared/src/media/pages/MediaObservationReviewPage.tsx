import { useCallback, useEffect, useMemo, useState } from 'react';
import { mediaApi } from '../services/mediaApi';
import type {
  MediaObservationCandidateDto,
  MediaObservationDto,
  MediaObservationSummaryDto,
} from '../services/mediaApi.types';

const emptySummary: MediaObservationSummaryDto = {
  totalUnresolved: 0,
  pending: 0,
  ambiguous: 0,
  noMatch: 0,
};

const statusStyles: Record<string, string> = {
  pending: 'bg-slate-100 text-slate-700 ring-slate-200',
  ambiguous: 'bg-amber-100 text-amber-800 ring-amber-200',
  no_match: 'bg-rose-100 text-rose-800 ring-rose-200',
  matched: 'bg-emerald-100 text-emerald-800 ring-emerald-200',
  rejected: 'bg-zinc-100 text-zinc-700 ring-zinc-200',
};

function formatStatus(status: string) {
  return status.replace(/_/g, ' ');
}

function formatScore(score: number) {
  return `${Math.round(score * 100)}%`;
}

function formatObservedAt(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

function SummaryStrip({ summary = emptySummary }: { summary?: MediaObservationSummaryDto | null }) {
  const values = summary ?? emptySummary;
  const items = [
    { label: 'Unresolved', value: values.totalUnresolved },
    { label: 'Ambiguous', value: values.ambiguous },
    { label: 'Pending', value: values.pending },
    { label: 'No match', value: values.noMatch },
  ];

  return (
    <div className="grid gap-3 sm:grid-cols-4">
      {items.map((item) => (
        <div key={item.label} className="rounded-lg border border-white/70 bg-white/80 px-4 py-3 shadow-sm">
          <p className="text-xs font-semibold tracking-wide text-slate-500 uppercase">{item.label}</p>
          <p className="mt-1 text-2xl font-semibold text-slate-950">{item.value}</p>
        </div>
      ))}
    </div>
  );
}

function ObservationHeader({
  observation,
  isBusy,
  onReject,
  onRetry,
}: {
  observation: MediaObservationDto;
  isBusy: boolean;
  onReject: (observationId: string) => void;
  onRetry: (observationId: string) => void;
}) {
  const statusClassName = statusStyles[observation.matchStatus] ?? statusStyles.pending;

  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-lg font-semibold text-slate-950">{observation.observedTitle}</h2>
          <span className={`rounded-full px-3 py-1 text-xs font-semibold ring-1 ${statusClassName}`}>
            {formatStatus(observation.matchStatus)}
          </span>
        </div>
        <p className="mt-1 text-sm text-slate-600">
          {observation.siteIdentifier}
          {observation.progressHint ? ` · ${observation.progressHint}` : ''}
          {' · '}
          {formatObservedAt(observation.observedAt)}
        </p>
      </div>
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={isBusy}
          onClick={() => onRetry(observation.observationId)}
          className="rounded-md border border-slate-300 bg-white px-3 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-400 disabled:cursor-not-allowed disabled:opacity-60"
        >
          Retry
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => onReject(observation.observationId)}
          className="rounded-md border border-rose-200 bg-rose-50 px-3 py-2 text-sm font-semibold text-rose-700 transition hover:border-rose-300 disabled:cursor-not-allowed disabled:opacity-60"
        >
          No match
        </button>
      </div>
    </div>
  );
}

function ObservationCandidates({
  observation,
  isBusy,
  onResolve,
}: {
  observation: MediaObservationDto;
  isBusy: boolean;
  onResolve: (observationId: string, candidateId: string) => void;
}) {
  if (observation.candidates.length === 0) {
    return (
      <p className="rounded-lg border border-dashed border-slate-300 bg-slate-50 px-4 py-5 text-sm text-slate-600">
        No candidates are stored for this observation.
      </p>
    );
  }

  return observation.candidates.map((candidate) => (
    <CandidateCard
      key={candidate.candidateId}
      candidate={candidate}
      isBusy={isBusy}
      onResolve={() => onResolve(observation.observationId, candidate.candidateId)}
    />
  ));
}

function CandidateCard({
  candidate,
  isBusy,
  onResolve,
}: {
  candidate: MediaObservationCandidateDto;
  isBusy: boolean;
  onResolve: () => void;
}) {
  return (
    <article className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-slate-950">{candidate.title}</p>
          <p className="mt-1 text-xs text-slate-500">
            {candidate.mediaKind} · {candidate.candidateSource.replace(/_/g, ' ')}
          </p>
        </div>
        <span className="rounded-full bg-slate-950 px-3 py-1 text-xs font-semibold text-white">
          {formatScore(candidate.score)}
        </span>
      </div>

      <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-600">
        {candidate.provider ? (
          <span className="rounded-full bg-slate-100 px-2.5 py-1">{candidate.provider}</span>
        ) : null}
        {candidate.providerMediaId ? (
          <span className="rounded-full bg-slate-100 px-2.5 py-1">{candidate.providerMediaId}</span>
        ) : null}
        {candidate.isAccepted ? (
          <span className="rounded-full bg-emerald-100 px-2.5 py-1 font-semibold text-emerald-700">
            Confirmed
          </span>
        ) : null}
      </div>

      {candidate.explanation ? (
        <p className="mt-3 text-sm text-slate-600">{candidate.explanation}</p>
      ) : null}

      <button
        type="button"
        disabled={isBusy}
        onClick={onResolve}
        className="mt-4 rounded-md bg-slate-950 px-4 py-2 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
      >
        Use match
      </button>
    </article>
  );
}

function ObservationCard({
  observation,
  busyObservationId,
  onResolve,
  onReject,
  onRetry,
}: {
  observation: MediaObservationDto;
  busyObservationId: string | null;
  onResolve: (observationId: string, candidateId: string) => void;
  onReject: (observationId: string) => void;
  onRetry: (observationId: string) => void;
}) {
  const isBusy = busyObservationId === observation.observationId;

  return (
    <article className="rounded-lg border border-white/70 bg-white/85 p-5 shadow-sm">
      <ObservationHeader
        observation={observation}
        isBusy={isBusy}
        onReject={onReject}
        onRetry={onRetry}
      />

      {observation.resolutionNotes ? (
        <p className="mt-4 rounded-md bg-slate-50 px-3 py-2 text-sm text-slate-600">{observation.resolutionNotes}</p>
      ) : null}

      {observation.lastMatchError ? (
        <p className="mt-3 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700">{observation.lastMatchError}</p>
      ) : null}

      <div className="mt-5 grid gap-3 lg:grid-cols-2">
        <ObservationCandidates observation={observation} isBusy={isBusy} onResolve={onResolve} />
      </div>
    </article>
  );
}

function useMediaObservationReviewState() {
  const [summary, setSummary] = useState<MediaObservationSummaryDto | null>(null);
  const [observations, setObservations] = useState<MediaObservationDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [busyObservationId, setBusyObservationId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    const [nextSummary, nextObservations] = await Promise.all([
      mediaApi.getObservationSummary(),
      mediaApi.getObservations({ limit: 100 }),
    ]);
    setSummary(nextSummary);
    setObservations(nextObservations);
  }, []);

  useEffect(() => {
    let isCurrent = true;
    setIsLoading(true);
    load()
      .catch((err: unknown) => {
        if (isCurrent) setError(err instanceof Error ? err.message : 'Failed to load media observations');
      })
      .finally(() => {
        if (isCurrent) setIsLoading(false);
      });

    return () => {
      isCurrent = false;
    };
  }, [load]);

  const runAction = useCallback(
    async (observationId: string, action: () => Promise<MediaObservationDto>) => {
      setBusyObservationId(observationId);
      setError(null);
      try {
        await action();
        await load();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to update media observation');
      } finally {
        setBusyObservationId(null);
      }
    },
    [load],
  );

  const sortedObservations = useMemo(
    () => [...observations].sort((a, b) => b.candidates.length - a.candidates.length),
    [observations],
  );

  return {
    summary,
    sortedObservations,
    isLoading,
    busyObservationId,
    error,
    runAction,
  };
}

function ObservationReviewContent({
  state,
}: {
  state: ReturnType<typeof useMediaObservationReviewState>;
}) {
  if (state.isLoading) {
    return (
      <div className="rounded-lg border border-white/70 bg-white/80 px-5 py-8 text-sm text-slate-600 shadow-sm">
        Loading media observations...
      </div>
    );
  }

  if (state.sortedObservations.length === 0) {
    return (
      <div className="rounded-lg border border-white/70 bg-white/80 px-5 py-8 text-sm text-slate-600 shadow-sm">
        No media observations need review.
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {state.sortedObservations.map((observation) => (
        <ObservationCard
          key={observation.observationId}
          observation={observation}
          busyObservationId={state.busyObservationId}
          onResolve={(observationId, candidateId) => void state.runAction(
            observationId,
            () => mediaApi.resolveObservation(observationId, { candidateId }),
          )}
          onReject={(observationId) => void state.runAction(
            observationId,
            () => mediaApi.rejectObservation(observationId),
          )}
          onRetry={(observationId) => void state.runAction(
            observationId,
            () => mediaApi.retryObservation(observationId),
          )}
        />
      ))}
    </div>
  );
}

export function MediaObservationReviewPage({ embedded = false }: { embedded?: boolean }) {
  const state = useMediaObservationReviewState();

  const contentClassName = `space-y-5 ${embedded ? '' : 'relative z-10 mx-auto max-w-320 px-6 pt-8 pb-16'}`;

  const content = (
    <div className={contentClassName}>
      <section>
        <p className="text-sm font-semibold tracking-wide text-slate-500 uppercase">Observation review</p>
        <h1 className="mt-2 text-3xl font-semibold text-slate-950">Resolve media matches</h1>
        <p className="mt-2 max-w-3xl text-sm text-slate-600">
          Choose a candidate when Cantaro is unsure, or mark the observation as no match. Confirmed choices apply to your account only.
        </p>
      </section>

      <SummaryStrip summary={state.summary} />

      {state.error ? (
        <div className="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{state.error}</div>
      ) : null}

      <ObservationReviewContent state={state} />
    </div>
  );

  if (embedded) return content;

  return (
    <div className="min-h-screen bg-slate-100 text-slate-950">
      {content}
    </div>
  );
}
