export function MarkerList({ markers, tone }: { markers: string[]; tone: 'version' | 'playback' }) {
  if (markers.length === 0) return null;
  const classes = tone === 'version' ? 'border-success-border bg-success-surface text-success-content' : 'border-warning-border bg-warning-surface text-warning-content';
  return <div className="mt-2 flex flex-wrap gap-2">{markers.map((marker) => <span key={`${tone}-${marker}`} className={`border px-2 py-1 text-[11px] font-semibold ${classes}`}>{marker}</span>)}</div>;
}
