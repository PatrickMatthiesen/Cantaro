import type { MediaLibraryListItemDto } from '../../services/mediaApi';
import type { LibraryEntryProgressSegments } from './LibraryEntryCardSections';

const PROGRESS_DIMENSION_CONFIG = {
    episode: {
        label: 'Ep',
        getCurrent: (entry: MediaLibraryListItemDto) => entry.progressEpisodes ?? 0,
        getTotal: (entry: MediaLibraryListItemDto) => entry.episodeCount,
    },
    chapter: {
        label: 'Ch',
        getCurrent: (entry: MediaLibraryListItemDto) => entry.progressChapters ?? 0,
        getTotal: (entry: MediaLibraryListItemDto) => entry.chapterCount,
    },
    volume: {
        label: 'Vol',
        getCurrent: (entry: MediaLibraryListItemDto) => entry.progressVolumes ?? 0,
        getTotal: (entry: MediaLibraryListItemDto) => entry.volumeCount,
    },
} as const;

const OPEN_ENDED_PROGRESS_FRACTION = 0.9;

function progressConfig(entry: MediaLibraryListItemDto) {
    if (!entry.primaryProgressDimension) {
        return null;
    }

    return PROGRESS_DIMENSION_CONFIG[entry.primaryProgressDimension as keyof typeof PROGRESS_DIMENSION_CONFIG] ?? null;
}

export function progressText(entry: MediaLibraryListItemDto): string {
    const config = progressConfig(entry);
    if (!config) {
        return '';
    }

    const current = config.getCurrent(entry);
    const total = config.getTotal(entry);
    return total ? `${config.label} ${current} / ${total}` : `${config.label} ${current} / ?`;
}

function unknownProgressVisualTotal(released: number, dimension: string): number {
    if (dimension !== 'episode') {
        return released / OPEN_ENDED_PROGRESS_FRACTION;
    }

    if (released < 12) {
        return 12;
    }

    if (released < 13) {
        return 13;
    }

    if (released < 24) {
        return 24;
    }

    return released / OPEN_ENDED_PROGRESS_FRACTION;
}

function knownProgressSegments(
    current: number,
    total: number,
    reportedReleased: number | undefined,
): LibraryEntryProgressSegments {
    const watched = Math.min(total, current);
    const released = reportedReleased === undefined
        ? watched
        : Math.max(watched, Math.min(total, reportedReleased));

    return {
        watched,
        releasedUnwatched: released - watched,
        remaining: total - released,
        total,
        visualTotal: total,
    };
}

function openEndedProgressSegments(
    current: number,
    reportedReleased: number | undefined,
    dimension: string,
): LibraryEntryProgressSegments | null {
    const released = reportedReleased === undefined ? current : Math.max(current, reportedReleased);
    if (released <= 0) {
        return null;
    }

    return {
        watched: current,
        releasedUnwatched: released - current,
        remaining: null,
        total: null,
        visualTotal: unknownProgressVisualTotal(released, dimension),
    };
}

export function progressSegments(entry: MediaLibraryListItemDto): LibraryEntryProgressSegments | null {
    const config = progressConfig(entry);
    const total = config?.getTotal(entry);
    if (!config) {
        return null;
    }

    const current = Math.max(0, config.getCurrent(entry));
    const reportedReleased = entry.primaryProgressDimension === 'episode' ? entry.releasedCount : undefined;

    if (total && total > 0) {
        return knownProgressSegments(current, total, reportedReleased);
    }

    return openEndedProgressSegments(current, reportedReleased, entry.primaryProgressDimension);
}
