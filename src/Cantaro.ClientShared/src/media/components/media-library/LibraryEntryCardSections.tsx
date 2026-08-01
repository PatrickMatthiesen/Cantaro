import type { MediaLibraryListItemDto } from '../../services/mediaApi';
import type { MediaLibraryDensity } from '../../pages/MediaLibraryPage';
import { Pill } from '../../../ui';

interface TopLeftBadge {
    label: string;
    detail?: string;
}

const DETAILS_CLASS_NAMES: Record<MediaLibraryDensity, {
    container: string;
    panel: string;
    title: string;
    originalTitle: string;
}> = {
    comfortable: {
        container: '',
        panel: 'px-3 py-2.5 sm:px-4 sm:py-3',
        title: 'text-base sm:text-lg md:text-xl',
        originalTitle: 'mt-0.5 text-xs sm:text-sm',
    },
    compact: {
        container: '',
        panel: 'px-2 py-1.5',
        title: 'text-xs',
        originalTitle: 'text-[0.65rem]',
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

function LibraryEntryReleaseBadge({ badge, density }: { badge: TopLeftBadge | null; density: MediaLibraryDensity }) {
    if (!badge) {
        return <span />;
    }

    const compact = density === 'compact';

    return (
        <Pill tone="info" size={compact ? 'compact' : 'regular'} className="min-w-0 max-w-40 gap-x-1 text-left shadow-lg">
            <span className="max-w-fit whitespace-nowrap font-semibold">{badge.label}</span>
            {!compact && badge.detail ? <span className="max-w-fit whitespace-nowrap opacity-75">{badge.detail}</span> : null}
        </Pill>
    );
}

function LibraryEntryProgressBadges({ progress, isConnected, density }: { progress: string; isConnected: boolean; density: MediaLibraryDensity }) {
    const compact = density === 'compact';

    return (
        <div className={`flex flex-col items-end ${compact ? 'gap-1' : 'gap-2'}`}>
            {progress ? (
                <Pill size={compact ? 'compact' : 'regular'} className="max-w-fit whitespace-nowrap shadow-lg">
                    {progress}
                </Pill>
            ) : null}
            {!isConnected ? (
                <Pill tone="warning" size={compact ? 'compact' : 'regular'} className="tracking-wide uppercase shadow-lg">
                    Not synced
                </Pill>
            ) : null}
        </div>
    );
}

export function LibraryEntryCardBadges({ topLeftBadge, progress, isConnected, density }: LibraryEntryCardBadgesProps) {
    const paddingClassName = density === 'compact' ? 'p-2' : 'p-3 sm:p-4';

    return (
        <div className={`absolute inset-x-0 top-0 flex items-start justify-between gap-1 ${paddingClassName}`}>
            <LibraryEntryReleaseBadge badge={topLeftBadge} density={density} />
            <LibraryEntryProgressBadges progress={progress} isConnected={isConnected} density={density} />
        </div>
    );
}

function LibraryEntryCompletionBar({ progress, density }: { progress: LibraryEntryProgressSegments | null; density: MediaLibraryDensity }) {
    const className = density === 'compact' ? 'mt-1.5 h-1.5' : 'mt-2 h-2 sm:mt-2.5 sm:h-2.5';

    if (!progress) {
        return null;
    }

    const watchedPercent = (progress.watched / progress.visualTotal) * 100;
    const releasedPercent = ((progress.watched + progress.releasedUnwatched) / progress.visualTotal) * 100;
    const description = progress.total === null
        ? `${progress.watched} watched, ${progress.releasedUnwatched} released and unwatched, total unknown`
        : `${progress.watched} watched, ${progress.releasedUnwatched} released and unwatched, ${progress.remaining} not yet released, ${progress.total} total`;
    const segments = (
        <>
            {progress.releasedUnwatched > 0 ? (
                <div
                    className="absolute inset-y-0 left-0 rounded-full bg-rose-400 transition-[width] duration-500 ease-[cubic-bezier(0.22,1,0.36,1)] motion-reduce:transition-none"
                    style={{ width: `${releasedPercent}%` }}
                />
            ) : null}
            <div
                className="absolute inset-y-0 left-0 rounded-full bg-linear-to-r from-cyan-300 to-sky-300 transition-[width] duration-500 ease-[cubic-bezier(0.22,1,0.36,1)] motion-reduce:transition-none"
                style={{ width: `${watchedPercent}%` }}
            />
        </>
    );

    if (progress.total === null) {
        const fadeMask = 'linear-gradient(to right, black 0%, black 90%, transparent 100%)';

        return (
            <div
                role="img"
                aria-label={`Episode progress: ${description}`}
                title={description}
                className={`${className} relative overflow-hidden rounded-l-full bg-slate-700/65 shadow-[inset_0_1px_2px_rgba(15,23,42,0.24)]`}
                style={{ maskImage: fadeMask, WebkitMaskImage: fadeMask }}
            >
                {segments}
            </div>
        );
    }

    return (
        <div
            role="progressbar"
            aria-label="Episode progress"
            aria-valuemin={0}
            aria-valuemax={progress.total}
            aria-valuenow={progress.watched}
            aria-valuetext={description}
            title={description}
            className={`${className} relative overflow-hidden rounded-full bg-slate-700/65 shadow-[inset_0_1px_2px_rgba(15,23,42,0.24)]`}
        >
            {segments}
        </div>
    );
}

export function LibraryEntryCardDetails({
    entry,
    progress,
    density,
}: LibraryEntryCardDetailsProps) {
    const classNames = DETAILS_CLASS_NAMES[density];

    return (
        <div className={`absolute inset-x-0 bottom-0 ${classNames.container}`}>
            <div className={`${classNames.panel} bg-black/30 backdrop-blur-md`}>
                <p className={`${classNames.title} line-clamp-2 leading-tight font-semibold text-white transition-colors group-hover:text-cyan-100`}>
                    {entry.canonicalTitle}
                </p>
                {/* Show original title if it's different from canonical title
                    TODO: Add a setting and only show if the user has enabled it, as it can add a lot of visual noise for some media with long titles
                */}
                {entry.originalTitle && entry.originalTitle !== entry.canonicalTitle ? (
                    <p className={`${classNames.originalTitle} line-clamp-1 text-white/64`}>{entry.originalTitle}</p>
                ) : null}

                <LibraryEntryCompletionBar progress={progress} density={density} />
            </div>
        </div>
    );
}
