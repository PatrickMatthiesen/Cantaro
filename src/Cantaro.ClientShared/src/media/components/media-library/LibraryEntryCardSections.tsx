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
        container: 'p-4',
        panel: 'rounded-3xl p-4',
        title: 'text-xl',
        originalTitle: 'mt-1 text-sm',
    },
    compact: {
        container: 'p-2',
        panel: 'rounded-2xl p-2',
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
    completion: number | null;
    density: MediaLibraryDensity;
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
    const paddingClassName = density === 'compact' ? 'p-2' : 'p-4';

    return (
        <div className={`absolute inset-x-0 top-0 flex items-start justify-between gap-1 ${paddingClassName}`}>
            <LibraryEntryReleaseBadge badge={topLeftBadge} density={density} />
            <LibraryEntryProgressBadges progress={progress} isConnected={isConnected} density={density} />
        </div>
    );
}

function LibraryEntryCompletionBar({ completion, density }: { completion: number | null; density: MediaLibraryDensity }) {
    const className = density === 'compact' ? 'mt-2 h-1' : 'mt-4 h-2';

    if (completion !== null) {
        return (
            <div className={`${className} overflow-hidden rounded-full bg-white/18`}>
                <div
                    className="h-full rounded-full bg-linear-to-r from-cyan-300 via-sky-400 to-rose-400 transition-all duration-500"
                    style={{ width: `${completion}%` }}
                />
            </div>
        );
    }

    return (
        <div className={`${className} overflow-hidden rounded-full bg-white/10`}>
            <div className="h-full w-1/3 rounded-full bg-linear-to-r from-white/55 to-white/15" />
        </div>
    );
}

export function LibraryEntryCardDetails({
    entry,
    completion,
    density,
}: LibraryEntryCardDetailsProps) {
    const classNames = DETAILS_CLASS_NAMES[density];

    return (
        <div className={`absolute inset-x-0 bottom-0 ${classNames.container}`}>
            <div className={`${classNames.panel} border border-white/16 bg-white/14 shadow-[0_18px_50px_rgba(15,23,42,0.38)] backdrop-blur-xl`}>
                <p className={`${classNames.title} line-clamp-2 leading-tight font-semibold text-white transition-colors group-hover:text-cyan-100`}>
                    {entry.canonicalTitle}
                </p>
                {/* Show original title if it's different from canonical title
                    TODO: Add a setting and only show if the user has enabled it, as it can add a lot of visual noise for some media with long titles
                */}
                {entry.originalTitle && entry.originalTitle !== entry.canonicalTitle ? (
                    <p className={`${classNames.originalTitle} line-clamp-1 text-white/64`}>{entry.originalTitle}</p>
                ) : null}

                <LibraryEntryCompletionBar completion={completion} density={density} />
            </div>
        </div>
    );
}
