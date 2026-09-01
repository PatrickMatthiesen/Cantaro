import type { PopupTabContextSnapshot } from './extensionAppTypes';
import type { ActiveTabContextState } from './extensionAppTypes';

function contextDetail(snapshot: Exclude<PopupTabContextSnapshot, { feature: 'unsupported' }>) {
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
    <div className="flex min-h-10 items-center gap-2 border-b border-border-subtle bg-surface-subtle px-4 text-xs text-content-muted" aria-live="polite">
      <span className="size-2 rounded-full bg-personal-accent" aria-hidden />
      <span className="font-semibold text-content">Current tab</span>
      <span className="truncate">{provider} · {detail || `${pageKind} page`}</span>
    </div>
  );
}
