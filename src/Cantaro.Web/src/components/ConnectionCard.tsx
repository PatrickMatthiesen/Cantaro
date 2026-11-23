import type { ReactNode } from 'react';

export type ConnectionState = 'connect' | 'connected' | 'reconnect' | 'manage' | 'retry';

const connectionLabels: Record<ConnectionState, string> = {
  connect: 'Connect',
  connected: 'Connected',
  reconnect: 'Reconnect',
  manage: 'Manage',
  retry: 'Retry',
};

function ConnectionButton({ state, busy, onAction }: { state: ConnectionState; busy: boolean; onAction: () => void }) {
  const color = state === 'connected'
    ? 'bg-success-surface text-success-content hover:bg-danger-surface hover:text-danger-content'
    : 'bg-personal-accent text-personal-accent-content hover:bg-personal-accent-hover';
  return (
    <button type="button" disabled={busy} onClick={onAction} className={`min-h-9 px-4 text-xs font-bold transition-colors ${color}`}>
      {busy ? 'Working…' : connectionLabels[state]}
    </button>
  );
}

export function ConnectionCard({ name, detail, icon, implemented, state, busy, onAction }: { name: string; detail: string; icon: ReactNode; implemented: boolean; state: ConnectionState; busy: boolean; onAction: () => void }) {
  return (
    <article className="flex items-center gap-4 border-t border-border-subtle py-4">
      {icon}
      <span className="min-w-0 flex-1"><strong className="block text-sm text-content">{name}</strong><span className="block truncate text-xs text-content-muted">{implemented ? detail : 'Coming later'}</span></span>
      {implemented
        ? <ConnectionButton state={state} busy={busy} onAction={onAction} />
        : <span className="bg-surface-subtle px-3 py-1.5 text-[10px] font-bold text-content-muted">Coming soon</span>}
    </article>
  );
}
