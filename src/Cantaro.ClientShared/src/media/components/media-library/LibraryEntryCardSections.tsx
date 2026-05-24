import type { MediaLibraryListItemDto } from '../../services/mediaApi';

interface TopLeftBadge {
    label: string;
    detail?: string;
}

export interface LibraryEntryCardBadgesProps {
    topLeftBadge: TopLeftBadge | null;
    progress: string;
    isConnected: boolean;
}

export interface LibraryEntryCardDetailsProps {
    entry: MediaLibraryListItemDto;
    completion: number | null;
}

function LibraryEntryReleaseBadge({ badge }: { badge: TopLeftBadge | null }) {
    if (!badge) {
        return <span />;
    }

    return (
        <span className="inline-flex min-w-0 max-w-40 gap-x-1 rounded-full bg-cyan-50/92 px-2 py-1 text-left text-xs text-slate-900 shadow-lg backdrop-blur-md">
            <span className="max-w-fit whitespace-nowrap font-semibold">{badge.label}</span>
            {badge.detail ? <span className="max-w-fit whitespace-nowrap text-slate-700">{badge.detail}</span> : null}
        </span>
    );
}

function LibraryEntryProgressBadges({ progress, isConnected }: { progress: string; isConnected: boolean }) {
    return (
        <div className="flex flex-col items-end gap-2">
            {progress ? (
                <span className="max-w-fit whitespace-nowrap rounded-full bg-white/90 px-2 py-1 text-xs font-semibold text-slate-900 shadow-lg backdrop-blur-md">
                    {progress}
                </span>
            ) : null}
            {!isConnected ? (
                <span className="rounded-full bg-amber-400/95 px-3 py-1 text-xs font-semibold tracking-wide text-slate-950 uppercase shadow-lg">
                    Not synced
                </span>
            ) : null}
        </div>
    );
}

export function LibraryEntryCardBadges({ topLeftBadge, progress, isConnected }: LibraryEntryCardBadgesProps) {
    return (
        <div className="absolute inset-x-0 top-0 flex items-start justify-between gap-1 p-4">
            <LibraryEntryReleaseBadge badge={topLeftBadge} />
            <LibraryEntryProgressBadges progress={progress} isConnected={isConnected} />
        </div>
    );
}

function LibraryEntryCompletionBar({ completion }: { completion: number | null }) {
    if (completion !== null) {
        return (
            <div className="mt-4 h-2 overflow-hidden rounded-full bg-white/18">
                <div
                    className="h-full rounded-full bg-linear-to-r from-cyan-300 via-sky-400 to-rose-400 transition-all duration-500"
                    style={{ width: `${completion}%` }}
                />
            </div>
        );
    }

    return (
        <div className="mt-4 h-2 overflow-hidden rounded-full bg-white/10">
            <div className="h-full w-1/3 rounded-full bg-linear-to-r from-white/55 to-white/15" />
        </div>
    );
}

export function LibraryEntryCardDetails({
    entry,
    completion,
}: LibraryEntryCardDetailsProps) {
    return (
        <div className="absolute inset-x-0 bottom-0 p-4">
            <div className="rounded-3xl border border-white/16 bg-white/14 p-4 shadow-[0_18px_50px_rgba(15,23,42,0.38)] backdrop-blur-xl">
                <p className="mt-3 line-clamp-2 text-xl leading-tight font-semibold text-white transition-colors group-hover:text-cyan-100">
                    {entry.canonicalTitle}
                </p>
                {/* Show original title if it's different from canonical title
                    TODO: Add a setting and only show if the user has enabled it, as it can add a lot of visual noise for some media with long titles
                */}
                {entry.originalTitle && entry.originalTitle !== entry.canonicalTitle ? (
                    <p className="mt-1 line-clamp-1 text-sm text-white/64">{entry.originalTitle}</p>
                ) : null}

                <LibraryEntryCompletionBar completion={completion} />
            </div>
        </div>
    );
}
