import type { ReactNode } from 'react';

export function SongSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-xl bg-surface-translucent p-3">
      <h3 className="mb-2 text-xs font-bold text-content-muted">{title}</h3>
      {children}
    </section>
  );
}
