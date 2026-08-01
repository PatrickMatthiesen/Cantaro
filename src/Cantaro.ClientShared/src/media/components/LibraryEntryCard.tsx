import { useState } from 'react';
import {
    type LibraryEntryProgressSegments,
    LibraryEntryCardBadges,
    LibraryEntryCardDetails,
} from './media-library/LibraryEntryCardSections';
import { formatRelativeReleaseTime } from '../services/mediaFormatting';
import type { MediaLibraryDensity } from '../pages/MediaLibraryPage';
import type { MediaLibraryListItemDto } from '../services/mediaApi';
import { MediaProviderIcon } from './MediaProviderIcon';

export interface LibraryEntryCardProps {
    entry: MediaLibraryListItemDto;
    onClick: () => void;
    density?: MediaLibraryDensity;
}

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

function progressText(entry: MediaLibraryListItemDto): string {
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

export function progressSegments(entry: MediaLibraryListItemDto): LibraryEntryProgressSegments | null {
    const config = progressConfig(entry);
    const total = config?.getTotal(entry);
    if (!config) {
        return null;
    }

    const current = Math.max(0, config.getCurrent(entry));
    const reportedReleased = entry.primaryProgressDimension === 'episode' ? entry.releasedCount : undefined;

    if (total && total > 0) {
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

    const watched = current;
    const released = reportedReleased === undefined ? watched : Math.max(watched, reportedReleased);
    if (released <= 0) {
        return null;
    }

    return {
        watched,
        releasedUnwatched: released - watched,
        remaining: null,
        total: null,
        visualTotal: unknownProgressVisualTotal(released, entry.primaryProgressDimension),
    };
}

function LibraryArtwork({ posterUrl, title }: { posterUrl?: string; title: string }) {
    const [failed, setFailed] = useState(false);

    if (!posterUrl || failed) {
        return (
            <div className="flex h-full w-full items-center justify-center bg-surface-subtle">
                <MediaProviderIcon providerId="anilist" className="h-8 w-8" aria-hidden />
                <span className="sr-only">{title} — no artwork available</span>
            </div>
        );
    }

    return (
        <img
            src={posterUrl}
            alt={`${title} cover art`}
            className="h-full w-full object-cover"
            onError={() => setFailed(true)}
        />
    );
}

export function LibraryEntryCard({ entry, onClick, density = 'comfortable' }: LibraryEntryCardProps) {
    const progress = progressText(entry);
    const progressBar = progressSegments(entry);
    const nextReleaseRelative = formatRelativeReleaseTime(entry.nextReleaseAt);
    const releaseBadgeLabel = entry.nextReleaseLabel ?? 'Next release';
    const topLeftBadge = nextReleaseRelative
        ? {
            label: releaseBadgeLabel,
            detail: nextReleaseRelative,
        }
        : null;

    return (
        <button
            type="button"
            onClick={onClick}
            className="group h-full w-full text-left transition-transform duration-300 hover:scale-[1.01] focus:outline-none focus-visible:ring-2 focus-visible:ring-focus"
        >
            <div className={`relative aspect-[0.72] overflow-hidden bg-slate-900 shadow-[0_18px_45px_rgba(15,23,42,0.22)] ${density === 'compact' ? 'rounded-2xl' : 'rounded-[1.75rem]'}`}>
                <LibraryArtwork posterUrl={entry.posterUrl} title={entry.canonicalTitle} />
                <div className="absolute inset-0 bg-linear-to-t from-slate-950/55 via-slate-900/20 to-transparent" aria-hidden />

                <LibraryEntryCardBadges
                    topLeftBadge={topLeftBadge}
                    progress={progress}
                    isConnected={entry.isConnected}
                    density={density}
                />

                <LibraryEntryCardDetails
                    entry={entry}
                    progress={progressBar}
                    density={density}
                />
            </div>
        </button>
    );
}
