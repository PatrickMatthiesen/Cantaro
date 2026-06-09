export function MusicEmptyPanel({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="rounded-3xl border border-dashed border-[#ded8f2] bg-white/54 p-6">
      <p className="font-black text-slate-950">{title}</p>
      <p className="mt-1 text-sm font-medium text-slate-500">{detail}</p>
    </div>
  );
}
