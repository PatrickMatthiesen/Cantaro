import type { MediaLibraryEntryDetailDto } from '../../services/mediaApi';
import type { MediaEntryDetailContentProps, ProgressSummary } from './mediaEntryDetailTypes';

export function releaseStatusLabel(dimension: string): string {
  const map: Record<string, string> = {
    airing: 'Currently Airing',
    finished: 'Finished',
    notYetAired: 'Not Yet Aired',
    not_yet_aired: 'Not Yet Aired',
    cancelled: 'Cancelled',
    hiatus: 'On Hiatus',
    unknown: 'Unknown',
  };
  return map[dimension] ?? dimension;
}

export function progressKindLabel(title: MediaLibraryEntryDetailDto['title']) {
  if (title.primaryProgressDimension === 'episode') {
    return title.episodeCount ? 'TV Series' : 'Episode tracking';
  }

  if (title.primaryProgressDimension === 'chapter') {
    return 'Manga';
  }

  if (title.primaryProgressDimension === 'volume') {
    return 'Volumes';
  }

  return releaseStatusLabel(title.releaseStatusDimension);
}

export function clampProgressValue(value: number, max?: number) {
  const lowerBoundedValue = Math.max(0, Math.round(value));
  return max ? Math.min(lowerBoundedValue, max) : lowerBoundedValue;
}

function displayProgressValue(value: number | undefined): number {
  return value ?? 0;
}

function displayProgressTotal(total: number | undefined): number | '?' {
  return total ?? '?';
}

function createProgressSummary(
  label: string,
  noun: string,
  action: string,
  unit: string,
  value: number | undefined,
  total: number | undefined,
): ProgressSummary {
  return {
    label,
    noun,
    value,
    total,
    progressLabel: `${action}: ${unit} ${displayProgressValue(value)} / ${displayProgressTotal(total)}`,
  };
}

export function getPrimaryProgressSummary(
  title: MediaLibraryEntryDetailDto['title'],
  progressEpisodes: number | undefined,
  progressChapters: number | undefined,
  progressVolumes: number | undefined,
): ProgressSummary {
  if (title.primaryProgressDimension === 'chapter') {
    return createProgressSummary('Chapters read', 'chapters', 'Read', 'Chapter', progressChapters, title.chapterCount);
  }

  if (title.primaryProgressDimension === 'volume') {
    return createProgressSummary('Volumes read', 'volumes', 'Read', 'Volume', progressVolumes, title.volumeCount);
  }

  return createProgressSummary('Watched', 'episodes', 'Watched', 'Episode', progressEpisodes, title.episodeCount);
}

export function getProgressPercent(summary: ProgressSummary) {
  if (!summary.total || summary.total <= 0) {
    return 0;
  }

  return Math.round((Math.min(summary.value ?? 0, summary.total) / summary.total) * 100);
}

export function getRemainingLabel(summary: ProgressSummary) {
  if (!summary.total) {
    return 'Total unknown';
  }

  const remaining = Math.max(summary.total - (summary.value ?? 0), 0);
  return remaining === 1 ? `1 ${summary.noun.slice(0, -1)} left` : `${remaining} ${summary.noun} left`;
}

export function getProgressCapabilities(title: MediaLibraryEntryDetailDto['title']) {
  const dim = title.primaryProgressDimension;
  return {
    supportsEpisodes: dim === 'episode',
    supportsChapters: dim === 'chapter',
    supportsVolumes: dim === 'volume' || dim === 'chapter',
  };
}

export function getEntryStatusChanged(props: Pick<
  MediaEntryDetailContentProps,
  'entry' | 'selectedStatus' | 'progressEpisodes' | 'progressChapters' | 'progressVolumes'
>) {
  return props.selectedStatus !== props.entry.normalizedStatus
    || props.progressEpisodes !== props.entry.progressEpisodes
    || props.progressChapters !== props.entry.progressChapters
    || props.progressVolumes !== props.entry.progressVolumes;
}

export function getStatusSaveLabel(isSavingStatus: boolean) {
  return isSavingStatus ? 'Saving...' : 'Save progress';
}

export function isStatusRefreshDisabled(
  canRefreshProgress: boolean,
  isSavingStatus: boolean,
  isRefreshingProgress: boolean,
) {
  return [!canRefreshProgress, isSavingStatus, isRefreshingProgress].some(Boolean);
}
