const MEDIA_KIND_LABELS: Record<string, string> = {
  anime: 'Anime',
  manga: 'Manga',
  lightNovel: 'Light Novel',
  oneShot: 'One Shot',
  novel: 'Novel',
};

const MEDIA_FORMAT_LABELS: Record<string, string> = {
  light_novel: 'Light Novel',
  manga: 'Manga',
  movie: 'Movie',
  music: 'Music',
  novel: 'Novel',
  ona: 'ONA',
  one_shot: 'One-shot',
  ova: 'OVA',
  special: 'Special',
  tv: 'TV Series',
  tv_short: 'TV Short',
};

function parseReleaseTimestamp(timestamp?: string): number | null {
  if (!timestamp) {
    return null;
  }

  const parsed = Date.parse(timestamp);
  return Number.isNaN(parsed) ? null : parsed;
}

function formatRelativeRelease(parsed: number): string {
  const diffMs = parsed - Date.now();
  const minute = 60 * 1000;
  const hour = 60 * minute;
  const day = 24 * hour;
  const week = 7 * day;
  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });

  if (Math.abs(diffMs) < hour) {
    return formatter.format(Math.round(diffMs / minute), 'minute');
  }

  if (Math.abs(diffMs) < day) {
    return formatter.format(Math.round(diffMs / hour), 'hour');
  }

  if (Math.abs(diffMs) < week) {
    return formatter.format(Math.round(diffMs / day), 'day');
  }

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(parsed);
}

export function formatRelativeReleaseTime(timestamp?: string): string | null {
  const parsed = parseReleaseTimestamp(timestamp);
  return parsed === null ? null : formatRelativeRelease(parsed);
}

export function mediaKindLabel(kind: string): string {
  return MEDIA_KIND_LABELS[kind] ?? kind;
}

export function mediaFormatLabel(format: string): string {
  return MEDIA_FORMAT_LABELS[format.toLowerCase()] ?? format;
}

export function formatNextReleaseDisplay(timestamp?: string): { relative: string; absolute: string } | null {
  const parsed = parseReleaseTimestamp(timestamp);
  if (parsed === null) {
    return null;
  }

  return {
    relative: formatRelativeRelease(parsed),
    absolute: new Intl.DateTimeFormat(undefined, {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    }).format(parsed),
  };
}
