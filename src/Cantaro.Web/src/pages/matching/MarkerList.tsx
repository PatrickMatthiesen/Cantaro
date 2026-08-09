export function MarkerList({ markers, tone }: { markers: string[]; tone: 'version' | 'playback' }) {
  if (markers.length === 0) return null;
  const classes = tone === 'version' ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-rose-200 bg-rose-50 text-rose-700';
  return <div className="mt-2 flex flex-wrap gap-2">{markers.map((marker) => <span key={`${tone}-${marker}`} className={`rounded-full border px-2 py-1 text-[11px] font-semibold ${classes}`}>{marker}</span>)}</div>;
}
