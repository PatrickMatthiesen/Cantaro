import { formatTimestamp } from '../../services/mediaRefreshCache';
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
    statusLabel: string;
    statusClassName: string;
    mediaKindLabel: string;
}

function LibraryEntryReleaseBadge({ badge }: { badge: TopLeftBadge | null }) {
    if (!badge) {
        return <span />;
    }

    return (
        <span className="inline-flex max-w-40 flex-col rounded-2xl bg-cyan-50/92 px-3 py-2 text-left text-[11px] text-slate-900 shadow-lg backdrop-blur-md">
            <span className="truncate font-semibold">{badge.label}</span>
            {badge.detail ? <span className="mt-0.5 truncate text-slate-700">{badge.detail}</span> : null}
        </span>
    );
}

function LibraryEntryProgressBadges({ progress, isConnected }: { progress: string; isConnected: boolean }) {
    return (
        <div className="flex flex-col items-end gap-2">
            {progress ? (
                <span className="rounded-full bg-white/90 px-3 py-1 text-xs font-semibold text-slate-900 shadow-lg backdrop-blur-md">
                    {progress}
                </span>
            ) : null}
            {!isConnected ? (
                <span className="rounded-full bg-amber-400/95 px-3 py-1 text-[11px] font-semibold tracking-wide text-slate-950 uppercase shadow-lg">
                    Not synced
                </span>
            ) : null}
        </div>
    );
}

function LibraryEntryMetadataLine({ entry, mediaKindLabel }: { entry: MediaLibraryListItemDto; mediaKindLabel: string }) {
    return (
        <div className="flex flex-wrap items-center gap-2 text-[11px] font-semibold tracking-[0.22em] text-white/78 uppercase">
            <span>{mediaKindLabel}</span>
            <span className="text-white/38">•</span>
            <span>{entry.provider}</span>
            {entry.rawListName ? (
                <>
                    <span className="text-white/38">•</span>
                    <span>{entry.rawListName}</span>
                </>
            ) : null}
        </div>
    );
}

function LibraryEntryStatusRow({ completion, statusLabel, statusClassName }: { completion: number | null; statusLabel: string; statusClassName: string }) {
    return (
        <div className="mt-4 flex flex-wrap items-center gap-2">
            <span className={`rounded-full px-3 py-1 text-[11px] font-semibold shadow-sm ${statusClassName}`}>
                {statusLabel}
            </span>
            {completion !== null ? (
                <span className="rounded-full bg-white/18 px-3 py-1 text-[11px] font-semibold text-white/88 shadow-sm backdrop-blur-md">
                    {Math.round(completion)}% complete
                </span>
            ) : null}
        </div>
    );
}

function LibraryEntrySyncTimestamp({ lastSyncedAt }: { lastSyncedAt?: string | null }) {
    if (!lastSyncedAt) {
        return null;
    }

    return (
        <p className="mt-3 text-xs text-white/62">
            Synced {formatTimestamp(lastSyncedAt) ?? new Date(lastSyncedAt).toLocaleString()}
        </p>
    );
}

export function LibraryEntryCardBadges({ topLeftBadge, progress, isConnected }: LibraryEntryCardBadgesProps) {
    return (
        <div className="absolute inset-x-0 top-0 flex items-start justify-between gap-3 p-4">
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
    statusLabel,
    statusClassName,
    mediaKindLabel,
}: LibraryEntryCardDetailsProps) {
    return (
        <div className="absolute inset-x-0 bottom-0 p-4">
            <div className="rounded-3xl border border-white/16 bg-white/14 p-4 shadow-[0_18px_50px_rgba(15,23,42,0.38)] backdrop-blur-xl">
                <LibraryEntryMetadataLine entry={entry} mediaKindLabel={mediaKindLabel} />

                <p className="mt-3 line-clamp-2 text-xl leading-tight font-semibold text-white transition-colors group-hover:text-cyan-100">
                    {entry.canonicalTitle}
                </p>
                {entry.originalTitle && entry.originalTitle !== entry.canonicalTitle ? (
                    <p className="mt-1 line-clamp-1 text-sm text-white/64">{entry.originalTitle}</p>
                ) : null}

                <LibraryEntryStatusRow completion={completion} statusLabel={statusLabel} statusClassName={statusClassName} />

                <LibraryEntryCompletionBar completion={completion} />
                <LibraryEntrySyncTimestamp lastSyncedAt={entry.lastSyncedAt} />
            </div>
        </div>
    );
}
