import type { ReactNode } from 'react';

export function MusicState({ title, detail, children }: { title: string; detail: string; children?: ReactNode }) {
  return (
    <div className="m-1 p-8 text-center">
      <h1 className="text-base font-bold text-content">{title}</h1>
      <p className="mx-auto mt-1 max-w-md text-sm leading-6 text-content-muted">{detail}</p>
      {children}
    </div>
  );
}
