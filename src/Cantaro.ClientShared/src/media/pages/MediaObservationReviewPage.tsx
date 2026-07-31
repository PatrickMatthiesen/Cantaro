import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
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
  pending: 'bg-surface-subtle text-content ring-border-subtle',
  ambiguous: 'bg-warning-surface text-warning-content ring-warning-border',
  no_match: 'bg-danger-surface text-danger-content ring-danger-border',
  matched: 'bg-success-surface text-success-content ring-success-border',
  rejected: 'bg-surface-subtle text-content-muted ring-border-subtle',
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
        <div key={item.label} className="rounded-lg border border-border-subtle bg-surface-translucent px-4 py-3 shadow-sm">
          <p className="text-xs font-semibold tracking-wide text-content-muted uppercase">{item.label}</p>
          <p className="mt-1 text-2xl font-semibold text-content">{item.value}</p>
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
          <h2 className="text-lg font-semibold text-content">{observation.observedTitle}</h2>
          <span className={`rounded-full px-3 py-1 text-xs font-semibold ring-1 ${statusClassName}`}>
            {formatStatus(observation.matchStatus)}
          </span>
        </div>
        <p className="mt-1 text-sm text-content-muted">
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
          className="rounded-md border border-border-strong bg-surface px-3 py-2 text-sm font-semibold text-content transition hover:border-focus disabled:cursor-not-allowed disabled:opacity-60"
        >
          Retry
        </button>
        <button
          type="button"
          disabled={isBusy}
          onClick={() => onReject(observation.observationId)}
          className="rounded-md border border-danger-border bg-danger-surface px-3 py-2 text-sm font-semibold text-danger-content transition hover:border-danger-content disabled:cursor-not-allowed disabled:opacity-60"
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
      <p className="rounded-lg border border-dashed border-border-strong bg-surface-subtle px-4 py-5 text-sm text-content-muted">
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
    <article className="rounded-lg border border-border-subtle bg-surface p-4 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-content">{candidate.title}</p>
          <p className="mt-1 text-xs text-content-muted">
            {candidate.mediaKind} · {candidate.candidateSource.replace(/_/g, ' ')}
          </p>
        </div>
        <span className="rounded-full bg-action px-3 py-1 text-xs font-semibold text-action-content">
          {formatScore(candidate.score)}
        </span>
      </div>

      <div className="mt-3 flex flex-wrap gap-2 text-xs text-content-muted">
        {candidate.provider ? (
          <span className="rounded-full bg-surface-subtle px-2.5 py-1">{candidate.provider}</span>
        ) : null}
        {candidate.providerMediaId ? (
          <span className="rounded-full bg-surface-subtle px-2.5 py-1">{candidate.providerMediaId}</span>
        ) : null}
        {candidate.isAccepted ? (
          <span className="rounded-full bg-success-surface px-2.5 py-1 font-semibold text-success-content">
            Confirmed
          </span>
        ) : null}
      </div>

      {candidate.explanation ? (
        <p className="mt-3 text-sm text-content-muted">{candidate.explanation}</p>
      ) : null}

      <button
        type="button"
        disabled={isBusy}
        onClick={onResolve}
        className="mt-4 rounded-md bg-action px-4 py-2 text-sm font-semibold text-action-content transition hover:bg-action-hover disabled:cursor-not-allowed disabled:opacity-60"
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
    <article className="rounded-lg border border-border-subtle bg-surface-translucent p-5 shadow-sm">
      <ObservationHeader
        observation={observation}
        isBusy={isBusy}
        onReject={onReject}
        onRetry={onRetry}
      />

      {observation.resolutionNotes ? (
        <p className="mt-4 rounded-md bg-surface-subtle px-3 py-2 text-sm text-content-muted">{observation.resolutionNotes}</p>
      ) : null}

      {observation.lastMatchError ? (
        <p className="mt-3 rounded-md bg-danger-surface px-3 py-2 text-sm text-danger-content">{observation.lastMatchError}</p>
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
      <div className="rounded-lg border border-border-subtle bg-surface-translucent px-5 py-8 text-sm text-content-muted shadow-sm">
        Loading media observations...
      </div>
    );
  }

  if (state.sortedObservations.length === 0) {
    return (
      <div className="rounded-lg border border-border-subtle bg-surface-translucent px-5 py-8 text-sm text-content-muted shadow-sm">
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

function ReviewPageIntro({ embedded, navigation }: { embedded: boolean; navigation?: ReactNode }) {
  if (embedded) {
    return navigation ? <div>{navigation}</div> : null;
  }

  return (
    <section>
      <p className="text-sm font-semibold tracking-wide text-content-muted uppercase">Observation review</p>
      <h1 className="mt-2 text-3xl font-semibold text-content">Resolve media matches</h1>
      <p className="mt-2 max-w-3xl text-sm text-content-muted">
        Choose a candidate when Cantaro is unsure, or mark the observation as no match. Confirmed choices apply to your account only.
      </p>
    </section>
  );
}

export function MediaObservationReviewPage({
  embedded = false,
  navigation,
}: {
  embedded?: boolean;
  navigation?: ReactNode;
}) {
  const state = useMediaObservationReviewState();

  const contentClassName = `space-y-5 ${embedded ? '' : 'relative z-10 mx-auto max-w-320 px-6 pt-8 pb-16'}`;

  const content = (
    <div className={contentClassName}>
      <ReviewPageIntro embedded={embedded} navigation={navigation} />

      <SummaryStrip summary={state.summary} />

      {state.error ? (
        <div className="rounded-lg border border-danger-border bg-danger-surface px-4 py-3 text-sm text-danger-content">{state.error}</div>
      ) : null}

      <ObservationReviewContent state={state} />
    </div>
  );

  if (embedded) return content;

  return (
    <div className="min-h-screen bg-canvas text-content">
      {content}
    </div>
  );
}
