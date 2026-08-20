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
  current: 'text-sky-300',
  completed: 'text-emerald-300',
  planned: 'text-amber-200',
  paused: 'text-amber-200',
  dropped: 'text-rose-300',
  repeating: 'text-cyan-300',
};

export function mediaLibraryStatusLabel(status: string, mediaKind: string): string {
  const label = STATUS_LABELS[status];
  if (label) return READING_MEDIA_KINDS.has(mediaKind) ? label.reading : label.watching;

  return status.replace(/[_-]+/g, ' ').replace(/^./, (character) => character.toUpperCase());
}

export function mediaLibraryStatusClassName(status: string): string {
  return STATUS_CLASS_NAMES[status] ?? 'text-white/75';
}
