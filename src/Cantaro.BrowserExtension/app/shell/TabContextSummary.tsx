import type { PopupTabContextSnapshot } from './extensionAppTypes';
import type { ActiveTabContextState } from './extensionAppTypes';

function contextDetail(snapshot: Exclude<PopupTabContextSnapshot, { feature: 'unsupported' }>) {
  if (snapshot.feature === 'music') return firstDetail(snapshot.title, snapshot.status);
  if (snapshot.pageKind === 'series') return firstDetail(snapshot.seriesTitle, snapshot.status);
  return firstDetail(snapshot.episodeTitle, snapshot.seriesTitle, snapshot.status);
}

function firstDetail(...values: Array<string | undefined>) {
  return values.find((value) => Boolean(value)) ?? '';
}

export function TabContextSummary({ context }: { context: ActiveTabContextState }) {
  if (context.status !== 'available' || context.snapshot.feature === 'unsupported') return null;

  const { pageKind, provider } = context.snapshot;
  const detail = contextDetail(context.snapshot);

  return (
    <div className="mt-2 flex min-h-9 items-center gap-2 rounded-xl bg-surface-subtle px-3 text-xs text-content-muted" aria-live="polite">
      <span className="size-2 rounded-full bg-accent" aria-hidden />
      <span className="font-semibold text-content">Current tab</span>
      <span className="truncate">{provider} · {detail || `${pageKind} page`}</span>
    </div>
  );
}
