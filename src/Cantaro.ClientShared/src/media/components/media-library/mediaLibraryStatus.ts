const READING_MEDIA_KINDS = new Set(['manga', 'lightNovel', 'oneShot']);

const STATUS_LABELS: Record<string, { watching: string; reading: string }> = {
  current: { watching: 'Watching', reading: 'Reading' },
  planned: { watching: 'Planning', reading: 'Planning' },
  paused: { watching: 'Paused', reading: 'Paused' },
  completed: { watching: 'Completed', reading: 'Completed' },
  dropped: { watching: 'Dropped', reading: 'Dropped' },
  repeating: { watching: 'Rewatching', reading: 'Rereading' },
};

const STATUS_CLASS_NAMES: Record<string, string> = {
  current: 'bg-violet-600 text-white',
  completed: 'bg-emerald-600 text-white',
  paused: 'bg-amber-500 text-slate-950',
  dropped: 'bg-rose-600 text-white',
  repeating: 'bg-cyan-600 text-white',
};

export function mediaLibraryStatusLabel(status: string, mediaKind: string): string {
  const label = STATUS_LABELS[status];
  if (label) return READING_MEDIA_KINDS.has(mediaKind) ? label.reading : label.watching;

  return status.replace(/[_-]+/g, ' ').replace(/^./, (character) => character.toUpperCase());
}

export function mediaLibraryStatusClassName(status: string): string {
  return STATUS_CLASS_NAMES[status] ?? 'bg-slate-700 text-white';
}
