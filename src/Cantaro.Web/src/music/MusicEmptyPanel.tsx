export function MusicEmptyPanel({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="rounded-[1.5rem] border border-dashed border-border-subtle bg-surface-translucent p-6">
      <p className="font-black text-content">{title}</p>
      <p className="mt-1 text-sm font-medium text-content-muted">{detail}</p>
    </div>
  );
}
