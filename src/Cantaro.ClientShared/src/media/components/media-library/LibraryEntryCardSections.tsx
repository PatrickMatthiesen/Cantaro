import type { MediaLibraryListItemDto } from '../../services/mediaApi';
import type { MediaLibraryDensity } from '../../pages/MediaLibraryPage';
import { mediaLibraryStatusClassName, mediaLibraryStatusLabel } from './mediaLibraryStatus';

interface TopLeftBadge {
    label: string;
    detail?: string;
}

const DETAILS_CLASS_NAMES: Record<MediaLibraryDensity, {
    panel: string;
    title: string;
    meta: string;
}> = {
    comfortable: {
        panel: 'px-3 pt-10 pb-3 sm:px-4 sm:pb-4',
        title: 'text-sm sm:text-base',
        meta: 'text-xs',
    },
    compact: {
        panel: 'px-2 pt-8 pb-2',
        title: 'text-xs',
        meta: 'text-[0.65rem]',
    },
};

export interface LibraryEntryCardBadgesProps {
    topLeftBadge: TopLeftBadge | null;
    progress: string;
    isConnected: boolean;
    density: MediaLibraryDensity;
}

export interface LibraryEntryCardDetailsProps {
    entry: MediaLibraryListItemDto;
    progress: LibraryEntryProgressSegments | null;
    density: MediaLibraryDensity;
}

export interface LibraryEntryProgressSegments {
    watched: number;
    releasedUnwatched: number;
    remaining: number | null;
    total: number | null;
    visualTotal: number;
}

function LibraryReleaseNote({ badge, compact }: { badge: TopLeftBadge | null; compact: boolean }) {
    if (!badge) return null;

    return (
        <div className="min-w-0 text-white">
            <p className={`${compact ? 'text-[0.65rem]' : 'text-xs'} line-clamp-1 font-semibold`}>{badge.label}</p>
            {!compact && badge.detail ? <p className="mt-0.5 text-xs text-white/70">{badge.detail}</p> : null}
        </div>
    );
}

function LibraryProgressNote({ progress, isConnected, compact }: { progress: string; isConnected: boolean; compact: boolean }) {
    return (
        <div className="shrink-0 text-right">
            {progress ? <p className={`${compact ? 'text-[0.65rem]' : 'text-xs'} font-semibold text-white`}>{progress}</p> : null}
            {!isConnected ? <p className="mt-0.5 text-[0.65rem] font-semibold text-amber-200">Sync unavailable</p> : null}
        </div>
    );
}

export function LibraryEntryCardBadges({ topLeftBadge, progress, isConnected, density }: LibraryEntryCardBadgesProps) {
    if (!topLeftBadge && !progress && isConnected) {
        return null;
    }

    const compact = density === 'compact';

    return (
        <div className={`absolute inset-x-0 top-0 flex items-start justify-between gap-2 bg-linear-to-b from-black/78 to-transparent ${compact ? 'p-2 pb-7' : 'p-3 pb-10'}`}>
            <LibraryReleaseNote badge={topLeftBadge} compact={compact} />
            <LibraryProgressNote progress={progress} isConnected={isConnected} compact={compact} />
        </div>
    );
}

function LibraryEntryCompletionBar({ progress, density }: { progress: LibraryEntryProgressSegments | null; density: MediaLibraryDensity }) {
    if (!progress) {
        return null;
    }

    const watchedPercent = (progress.watched / progress.visualTotal) * 100;
    const releasedPercent = ((progress.watched + progress.releasedUnwatched) / progress.visualTotal) * 100;
    const description = progress.total === null
        ? `${progress.watched} watched, ${progress.releasedUnwatched} released and unwatched, total unknown`
        : `${progress.watched} watched, ${progress.releasedUnwatched} released and unwatched, ${progress.remaining} not yet released, ${progress.total} total`;
    const className = density === 'compact' ? 'mt-2 h-1' : 'mt-3 h-1.5';
    const segments = (
        <>
            {progress.releasedUnwatched > 0 ? (
                <span
                    className="absolute inset-y-0 left-0 bg-amber-300 transition-[width] duration-500 motion-reduce:transition-none"
                    style={{ width: `${releasedPercent}%` }}
                />
            ) : null}
            <span
                className="absolute inset-y-0 left-0 bg-personal-accent transition-[width] duration-500 motion-reduce:transition-none"
                style={{ width: `${watchedPercent}%` }}
            />
        </>
    );

    if (progress.total === null) {
        const fadeMask = 'linear-gradient(to right, black 0%, black 90%, transparent 100%)';

        return (
            <span
                role="img"
                aria-label={`Title progress: ${description}`}
                title={description}
                className={`${className} relative block overflow-hidden bg-white/25`}
                style={{ maskImage: fadeMask, WebkitMaskImage: fadeMask }}
            >
                {segments}
            </span>
        );
    }

    return (
        <span
            role="progressbar"
            aria-label="Title progress"
            aria-valuemin={0}
            aria-valuemax={progress.total}
            aria-valuenow={progress.watched}
            aria-valuetext={description}
            title={description}
            className={`${className} relative block overflow-hidden bg-white/25`}
        >
            {segments}
        </span>
    );
}

export function LibraryEntryCardDetails({ entry, progress, density }: LibraryEntryCardDetailsProps) {
    const classNames = DETAILS_CLASS_NAMES[density];

    return (
        <div className={`absolute inset-x-0 bottom-0 bg-linear-to-t from-black/95 via-black/76 to-transparent ${classNames.panel}`}>
            <p className={`${mediaLibraryStatusClassName(entry.status)} ${classNames.meta} mb-1 font-semibold`}>
                {mediaLibraryStatusLabel(entry.status, entry.mediaKind)}
            </p>
            <p className={`${classNames.title} line-clamp-2 leading-tight font-bold text-white transition-colors group-hover:text-amber-200`}>
                {entry.canonicalTitle}
            </p>
            <LibraryEntryCompletionBar progress={progress} density={density} />
        </div>
    );
}
