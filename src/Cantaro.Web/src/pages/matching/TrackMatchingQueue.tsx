import { useCallback, useEffect, useState } from 'react';
import { matchingApi, type TrackMatchWorkItemResponse, type TrackMatchWorkPageResponse } from '@cantaro/client-shared/music';

function formatCountdown(target: string, now: string) {
  const remainingSeconds = Math.max(0, Math.ceil((new Date(target).getTime() - new Date(now).getTime()) / 1000));
  if (remainingSeconds < 60) return `${remainingSeconds}s`;
  const remainingMinutes = Math.ceil(remainingSeconds / 60);
  if (remainingMinutes < 60) return `${remainingMinutes}m`;
  const hours = Math.floor(remainingMinutes / 60);
  const minutes = remainingMinutes % 60;
  if (hours < 24) return `${hours}h ${minutes}m`;
  return `${Math.ceil(remainingMinutes / (60 * 24))}d`;
}

function queueStatusLabel(item: TrackMatchWorkItemResponse, isNext: boolean, data: TrackMatchWorkPageResponse) {
  if (isNext) {
    const effectiveStart = data.providerNotBefore
      && new Date(data.providerNotBefore).getTime() > new Date(item.nextAttemptAt).getTime()
      ? data.providerNotBefore
      : item.nextAttemptAt;
    return formatCountdown(effectiveStart, data.asOf);
  }
  const status = item.queueStatus;
  if (status === 'processing') return 'Processing';
  if (status === 'scheduled') return 'Scheduled';
  return 'Ready';
}

function queueStatusClasses(status: TrackMatchWorkItemResponse['queueStatus']) {
  if (status === 'processing') return 'bg-info-surface text-info-content';
  if (status === 'scheduled') return 'bg-warning-surface text-warning-content';
  return 'bg-success-surface text-success-content';
}

function formatTime(value?: string) {
  return value ? new Date(value).toLocaleString() : '—';
}

function cooldownMessage(data: TrackMatchWorkPageResponse) {
  const until = formatTime(data.providerNotBefore);
  return data.providerCooldownSource === 'retry-after'
    ? `MusicBrainz requested a cooldown until ${until}. Ready items will resume automatically.`
    : `MusicBrainz is unavailable; Cantaro is backing off until ${until}. Ready items will resume automatically.`;
}

function QueueStats({ data }: { data: TrackMatchWorkPageResponse }) {
  return <dl className="grid grid-cols-2 border-y border-border-subtle md:grid-cols-4">{[
    ['Total queued', data.totalCount], ['Ready', data.readyCount], ['Scheduled', data.scheduledCount], ['Processing', data.processingCount],
  ].map(([label, value]) => <div key={label} className="py-4 md:border-r md:border-border-subtle md:px-5 md:last:border-r-0"><dt className="text-sm text-content-muted">{label}</dt><dd className="mt-1 text-2xl font-bold text-content">{value}</dd></div>)}</dl>;
}

function QueueItem({
  item,
  isNext,
  data,
  isMarkingNotMusic,
  onMarkNotMusic,
}: {
  item: TrackMatchWorkItemResponse;
  isNext: boolean;
  data: TrackMatchWorkPageResponse;
  isMarkingNotMusic: boolean;
  onMarkNotMusic: (item: TrackMatchWorkItemResponse) => void;
}) {
  return <article className="border-y border-border-subtle bg-surface-translucent px-4 py-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0"><h3 className="truncate text-base font-bold text-content">{item.title}</h3><p className="mt-1 text-sm text-content-muted">{item.artist ?? 'Unknown artist'} · {item.sourceType}</p><p className="mt-1 text-xs text-content-subtle">{item.externalId}</p></div>
      <div className="flex flex-wrap items-center justify-end gap-2">
        <span className={`px-3 py-1 text-xs font-bold ${queueStatusClasses(item.queueStatus)}`}>{queueStatusLabel(item, isNext, data)}</span>
        <button
          type="button"
          onClick={() => onMarkNotMusic(item)}
          disabled={isMarkingNotMusic}
          aria-label={`Mark ${item.title} as not music`}
          className="min-h-8 border border-danger-border px-3 text-xs font-semibold text-danger-content transition-colors hover:bg-danger-surface focus-visible:outline-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isMarkingNotMusic ? 'Marking…' : 'Mark as not music'}
        </button>
      </div>
    </div>
    <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2"><div><dt className="text-content-muted">Retry cycle</dt><dd className="font-semibold text-content">{item.retryCount} retries</dd></div><div><dt className="text-content-muted">Lifetime attempts</dt><dd className="font-semibold text-content">{item.lifetimeAttemptCount}</dd></div></dl>
    {item.lastError ? <p className="mt-3 text-sm text-danger-content">Last error: {item.lastError}</p> : null}
  </article>;
}

function QueuePagination({ data, onPageChange }: { data: TrackMatchWorkPageResponse; onPageChange: (page: number) => void }) {
  if (data.totalPages <= 1) return null;
  return <nav className="flex items-center justify-between gap-3 border-y border-border-subtle py-3" aria-label="Matching work queue pages"><button type="button" disabled={data.page <= 1} onClick={() => onPageChange(data.page - 1)} className="min-h-10 border border-border-strong px-3 disabled:opacity-40">Previous</button><span className="text-sm text-content-muted">Page {data.page} of {data.totalPages}</span><button type="button" disabled={data.page >= data.totalPages} onClick={() => onPageChange(data.page + 1)} className="min-h-10 bg-personal-accent px-3 text-personal-accent-content disabled:opacity-40">Next</button></nav>;
}

function useTrackMatchingQueueData() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<TrackMatchWorkPageResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => {
    try {
      const response = await matchingApi.getWorkQueue(page);
      setData(response);
      setError(null);
      if (response.page !== page) setPage(response.page);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Failed to load matching queue');
    }
  }, [page]);

  useEffect(() => {
    void load();
    const interval = window.setInterval(() => { if (!document.hidden) void load(); }, 2000);
    return () => window.clearInterval(interval);
  }, [load]);

  const [markingObservationId, setMarkingObservationId] = useState<string | null>(null);
  const markNotMusic = useCallback(async (item: TrackMatchWorkItemResponse) => {
    const confirmation = `Mark “${item.title}” as not music?\n\nIt will be removed from the matching queue and will not be queued again.`;
    if (!window.confirm(confirmation)) return;

    setMarkingObservationId(item.observationId);
    setError(null);
    try {
      await matchingApi.markNotMusic(item.observationId);
      await load();
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : 'Failed to mark item as not music');
    } finally {
      setMarkingObservationId(null);
    }
  }, [load]);

  return { data, error, load, markNotMusic, markingObservationId, setPage };
}

function QueueRows({
  data,
  markingObservationId,
  onMarkNotMusic,
}: {
  data: TrackMatchWorkPageResponse;
  markingObservationId: string | null;
  onMarkNotMusic: (item: TrackMatchWorkItemResponse) => void;
}) {
  if (data.items.length === 0) return <div className="border-y border-border-subtle py-8"><h3 className="text-xl font-semibold text-content">No matching work is scheduled</h3><p className="mt-2 text-sm text-content-muted">New unresolved imports and manual retries will appear here.</p></div>;
  const nextObservationId = data.page === 1
    ? data.items.find((item) => item.queueStatus !== 'processing')?.observationId
    : undefined;
  return <section className="space-y-3">{data.items.map((item) => <QueueItem key={item.observationId} item={item} isNext={item.observationId === nextObservationId} data={data} isMarkingNotMusic={markingObservationId === item.observationId} onMarkNotMusic={onMarkNotMusic} />)}</section>;
}

function TrackMatchingQueueContent({ data, error, markingObservationId, onMarkNotMusic, onRefresh, onPageChange }: { data: TrackMatchWorkPageResponse; error: string | null; markingObservationId: string | null; onMarkNotMusic: (item: TrackMatchWorkItemResponse) => void; onRefresh: () => void; onPageChange: (page: number) => void }) {
  return <div className="space-y-4"><div className="flex flex-wrap items-end justify-between gap-3"><div><h2 className="text-2xl font-bold text-content">Matching work queue</h2><p className="mt-1 text-sm text-content-muted">Durable work waiting for the canonical track matcher. Updates every two seconds.</p></div><button type="button" onClick={onRefresh} className="min-h-10 border border-border-strong px-3 text-sm font-semibold text-content">Refresh now</button></div><QueueStats data={data} />{data.providerNotBefore ? <p className="border-y border-warning-border bg-warning-surface p-3 text-sm font-semibold text-warning-content">{cooldownMessage(data)}</p> : null}{error ? <p className="text-sm text-danger-content">{error}</p> : null}<QueuePagination data={data} onPageChange={onPageChange} /><QueueRows data={data} markingObservationId={markingObservationId} onMarkNotMusic={onMarkNotMusic} /><QueuePagination data={data} onPageChange={onPageChange} /></div>;
}

export function TrackMatchingQueuePanel() {
  const queue = useTrackMatchingQueueData();
  if (!queue.data && !queue.error) return <p className="py-8 text-sm text-content-muted">Loading matching queue…</p>;
  if (!queue.data) return <p className="border-y border-danger-border bg-danger-surface p-4 text-danger-content">{queue.error}</p>;
  return <TrackMatchingQueueContent data={queue.data} error={queue.error} markingObservationId={queue.markingObservationId} onMarkNotMusic={(item) => void queue.markNotMusic(item)} onRefresh={() => void queue.load()} onPageChange={queue.setPage} />;
}
