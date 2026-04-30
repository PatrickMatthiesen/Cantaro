import { useState } from 'react';
import { GlassCard } from './ui/GlassComponents';
import {
    LibraryEntryCardBadges,
    LibraryEntryCardDetails,
} from './media-library/LibraryEntryCardSections';
import { formatRelativeReleaseTime, mediaKindLabel } from '../services/mediaFormatting';
import type { MediaLibraryListItemDto } from '../services/mediaApi';

export interface LibraryEntryCardProps {
    entry: MediaLibraryListItemDto;
    onClick: () => void;
}

const STATUS_COLOR_MAP: Record<string, string> = {
    current: 'bg-emerald-100 text-emerald-800',
    completed: 'bg-blue-100 text-blue-800',
    planning: 'bg-violet-100 text-violet-800',
    paused: 'bg-amber-100 text-amber-800',
    dropped: 'bg-rose-100 text-rose-800',
    repeating: 'bg-cyan-100 text-cyan-800',
};

const NORMALIZED_STATUS_LABELS: Record<string, string> = {
    current: 'Watching / Reading',
    completed: 'Completed',
    planning: 'Planning',
    paused: 'Paused',
    dropped: 'Dropped',
    repeating: 'Rewatching / Rereading',
};

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

function statusLabel(status: string): string {
    return NORMALIZED_STATUS_LABELS[status] ?? status;
}

function statusColor(status: string): string {
    return STATUS_COLOR_MAP[status] ?? 'bg-gray-100 text-gray-700';
}

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
    return total ? `${config.label} ${current} / ${total}` : `${config.label} ${current}`;
}

function progressPercent(entry: MediaLibraryListItemDto): number | null {
    const config = progressConfig(entry);
    const total = config?.getTotal(entry);
    if (!config || !total || total <= 0) {
        return null;
    }

    return Math.max(0, Math.min(100, (config.getCurrent(entry) / total) * 100));
}

function LibraryArtwork({ posterUrl, title }: { posterUrl?: string; title: string }) {
    const [failed, setFailed] = useState(false);

    if (!posterUrl || failed) {
        return (
            <div className="flex h-full w-full items-center justify-center bg-linear-to-br from-indigo-100 to-purple-100">
                <span className="text-2xl" aria-hidden>🎌</span>
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

export function LibraryEntryCard({ entry, onClick }: LibraryEntryCardProps) {
    const progress = progressText(entry);
    const completion = progressPercent(entry);
    const nextReleaseRelative = formatRelativeReleaseTime(entry.nextReleaseAt);
    const releaseBadgeLabel = entry.nextReleaseLabel ?? 'Next release';
    const topLeftBadge = nextReleaseRelative
        ? {
            label: releaseBadgeLabel,
            detail: nextReleaseRelative,
        }
        : null;

    return (
        <button type="button" onClick={onClick} className="h-full w-full text-left">
            <GlassCard interactive className="group h-full p-0 transition duration-500">
                <div className="relative aspect-[0.72] min-h-80 overflow-hidden rounded-[1.75rem]">
                    <LibraryArtwork posterUrl={entry.posterUrl} title={entry.canonicalTitle} />
                    <div className="absolute inset-0 bg-linear-to-t from-slate-950 via-slate-900/30 to-slate-900/10" aria-hidden />

                    <LibraryEntryCardBadges topLeftBadge={topLeftBadge} progress={progress} isConnected={entry.isConnected} />

                    <LibraryEntryCardDetails
                        entry={entry}
                        completion={completion}
                        statusLabel={statusLabel(entry.normalizedStatus)}
                        statusClassName={statusColor(entry.normalizedStatus)}
                        mediaKindLabel={mediaKindLabel(entry.mediaKind)}
                    />
                </div>
            </GlassCard>
        </button>
    );
}
