import type { ReactNode } from 'react';

export function ConnectionCard({ name, detail, icon, implemented, connected, reconnect, busy, onAction, actionLabel }: { name: string; detail: string; icon: ReactNode; implemented: boolean; connected: boolean; reconnect: boolean; busy: boolean; onAction: () => void; actionLabel?: string }) {
  return (
    <article className="flex items-center gap-4 border-t border-border-subtle py-4">
      {icon}
      <span className="min-w-0 flex-1"><strong className="block text-sm text-content">{name}</strong><span className="block truncate text-xs text-content-muted">{implemented ? detail : 'Coming later'}</span></span>
      {implemented ? <button type="button" disabled={busy} onClick={onAction} className={`min-h-9 px-4 text-xs font-bold transition-colors ${connected && !reconnect && !actionLabel ? 'bg-success-surface text-success-content hover:bg-danger-surface hover:text-danger-content' : 'bg-personal-accent text-personal-accent-content hover:bg-personal-accent-hover'}`}>{busy ? 'Working…' : actionLabel ?? (reconnect ? 'Reconnect' : connected ? 'Connected' : 'Connect')}</button> : <span className="bg-surface-subtle px-3 py-1.5 text-[10px] font-bold text-content-muted">Coming soon</span>}
    </article>
  );
}
