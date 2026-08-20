import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { AlertTriangle, CheckCircle2, RefreshCw, XCircle } from 'lucide-react';
import { ActionButton } from '../../ui';
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
  pending: 'text-content-muted',
  ambiguous: 'text-warning-content',
  no_match: 'text-danger-content',
  matched: 'text-success-content',
  rejected: 'text-content-muted',
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
    <dl className="grid border-y border-border-subtle sm:grid-cols-4 sm:divide-x sm:divide-border-subtle">
      {items.map((item) => (
        <div key={item.label} className="flex items-baseline justify-between gap-4 border-b border-border-subtle px-4 py-4 last:border-b-0 sm:block sm:border-b-0">
          <dt className="text-sm font-semibold text-content-muted">{item.label}</dt>
          <dd className="text-2xl font-black tabular-nums text-content sm:mt-1">{item.value}</dd>
        </div>
      ))}
    </dl>
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
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-lg font-black text-content">{observation.observedTitle}</h2>
          <span className={`inline-flex items-center gap-1.5 text-xs font-bold ${statusClassName}`}>
            {observation.matchStatus === 'ambiguous' ? <AlertTriangle className="size-3.5" aria-hidden /> : null}
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
        <ActionButton
          tone="ghost"
          disabled={isBusy}
          onClick={() => onRetry(observation.observationId)}
        >
          <RefreshCw className="size-4" aria-hidden />
          Retry
        </ActionButton>
        <ActionButton
          tone="danger"
          disabled={isBusy}
          onClick={() => onReject(observation.observationId)}
        >
          <XCircle className="size-4" aria-hidden />
          No match
        </ActionButton>
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
      <p className="border-t border-border-subtle py-5 text-sm text-content-muted">
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
    <article className="border-t border-border-subtle py-5 lg:px-5 lg:[&:nth-child(2n)]:border-l lg:[&:nth-child(2n)]:border-l-border-subtle">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-content">{candidate.title}</p>
          <p className="mt-1 text-xs text-content-muted">
            {candidate.mediaKind} · {candidate.candidateSource.replace(/_/g, ' ')}
          </p>
        </div>
        <span className="text-sm font-black tabular-nums text-content">
          {formatScore(candidate.score)} match
        </span>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-content-muted">
        {candidate.provider ? (
          <span>{candidate.provider}</span>
        ) : null}
        {candidate.providerMediaId ? (
          <><span aria-hidden>·</span><span>{candidate.providerMediaId}</span></>
        ) : null}
        {candidate.isAccepted ? (
          <span className="inline-flex items-center gap-1 font-semibold text-success-content">
            <CheckCircle2 className="size-3.5" aria-hidden />
            Confirmed
          </span>
        ) : null}
      </div>

      {candidate.explanation ? (
        <p className="mt-3 text-sm text-content-muted">{candidate.explanation}</p>
      ) : null}

      <ActionButton
        tone="personal"
        disabled={isBusy}
        onClick={onResolve}
        className="mt-4"
      >
        Use match
      </ActionButton>
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
    <article className="py-7 first:pt-0 last:pb-0">
      <ObservationHeader
        observation={observation}
        isBusy={isBusy}
        onReject={onReject}
        onRetry={onRetry}
      />

      {observation.resolutionNotes ? (
        <p className="mt-4 bg-warning-surface px-3 py-2 text-sm font-semibold text-warning-content">{observation.resolutionNotes}</p>
      ) : null}

      {observation.lastMatchError ? (
        <p className="mt-3 bg-danger-surface px-3 py-2 text-sm font-semibold text-danger-content">{observation.lastMatchError}</p>
      ) : null}

      <div className="mt-5 grid lg:grid-cols-2">
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
      <div className="border-y border-border-subtle py-8 text-sm font-semibold text-content-muted" aria-live="polite">
        Loading media observations…
      </div>
    );
  }

  if (state.sortedObservations.length === 0) {
    return (
      <div className="border-y border-border-subtle py-8 text-sm font-semibold text-content-muted">
        No media observations need review.
      </div>
    );
  }

  return (
    <div className="divide-y divide-border-subtle border-y border-border-subtle">
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
      <p className="text-sm font-semibold text-content-muted">Cantaro · Media</p>
      <h1 className="mt-1 text-3xl font-black text-content sm:text-4xl">Resolve media matches</h1>
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

  const contentClassName = `space-y-6 ${embedded ? '' : 'relative z-10 mx-auto max-w-320 px-6 pt-8 pb-16'}`;

  const content = (
    <div className={contentClassName}>
      <ReviewPageIntro embedded={embedded} navigation={navigation} />

      <SummaryStrip summary={state.summary} />

      {state.error ? (
        <div className="border-y border-danger-border bg-danger-surface px-4 py-3 text-sm font-semibold text-danger-content" role="alert">{state.error}</div>
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
