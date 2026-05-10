import { GlassCard } from '../../../ui';
import { DetailArtwork, SanitizedSynopsis } from './EntryDisplayPrimitives';
import { formatNextReleaseDisplay } from '../../services/mediaFormatting';
import type { MediaLibraryEntryDetailDto } from '../../services/mediaApi';

const RELEASE_STATUS_COLOR_MAP: Record<string, string> = {
    airing: 'bg-emerald-100 text-emerald-800',
    finished: 'bg-blue-100 text-blue-800',
    notYetAired: 'bg-violet-100 text-violet-800',
    not_yet_aired: 'bg-violet-100 text-violet-800',
    cancelled: 'bg-rose-100 text-rose-800',
    hiatus: 'bg-amber-100 text-amber-800',
};

function releaseStatusLabel(dimension: string): string {
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

function releaseStatusColor(dimension: string): string {
    return RELEASE_STATUS_COLOR_MAP[dimension] ?? 'bg-gray-100 text-gray-600';
}

function EntryReleaseBadge({
    label,
    nextRelease,
}: {
    label?: string;
    nextRelease: NonNullable<ReturnType<typeof formatNextReleaseDisplay>>;
}) {
    return (
        <div className="shrink-0 self-start rounded-2xl border border-cyan-100 bg-cyan-50 px-4 py-3 text-right shadow-sm">
            <p className="text-[11px] font-semibold tracking-[0.18em] text-cyan-700 uppercase">
                {label ?? 'Next release'}
            </p>
            <p className="mt-1 text-sm font-semibold text-slate-900">{nextRelease.relative}</p>
            <p className="mt-1 text-xs text-slate-500">{nextRelease.absolute}</p>
        </div>
    );
}

function EntryCounts({ title }: { title: MediaLibraryEntryDetailDto['title'] }) {
    const counts = [
        title.episodeCount ? `${title.episodeCount} episodes` : null,
        title.chapterCount ? `${title.chapterCount} chapters` : null,
        title.volumeCount ? `${title.volumeCount} volumes` : null,
    ].filter((value): value is string => Boolean(value));

    return (
        <div className="flex flex-wrap gap-3">
            {counts.length > 0
                ? counts.map((count) => <span key={count} className="text-sm text-gray-600">{count}</span>)
                : <span className="text-sm text-gray-400 italic">Count unknown</span>}
        </div>
    );
}

function EntryProviderStatus({ rawStatus, rawListName }: { rawStatus?: string | null; rawListName?: string | null }) {
    if (!rawStatus && !rawListName) {
        return null;
    }

    return <p className="text-xs text-gray-400">Provider status: {rawListName ?? rawStatus}</p>;
}

export interface EntryOverviewCardProps {
    entry: MediaLibraryEntryDetailDto;
    nextRelease: ReturnType<typeof formatNextReleaseDisplay>;
}

// fallow-ignore-next-line complexity
export function EntryOverviewCard({ entry, nextRelease }: EntryOverviewCardProps) {
    const { title } = entry;

    return (
        <GlassCard className="overflow-visible">
            <div className="flex flex-col gap-6 p-6 sm:flex-row">
                <div className="h-48 w-32 shrink-0 overflow-hidden rounded-2xl sm:h-56 sm:w-40">
                    <DetailArtwork posterUrl={title.posterUrl} title={title.canonicalTitle} />
                </div>

                <div className="min-w-0 flex-1 space-y-3">
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                        <div className="min-w-0">
                            <h1 className="text-2xl leading-tight font-bold text-gray-900">{title.canonicalTitle}</h1>
                            {title.originalTitle && title.originalTitle !== title.canonicalTitle ? (
                                <p className="mt-1 text-sm text-gray-500">{title.originalTitle}</p>
                            ) : null}
                        </div>

                        {nextRelease ? <EntryReleaseBadge label={entry.nextReleaseLabel ?? undefined} nextRelease={nextRelease} /> : null}
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                        {title.startYear ? (
                            <span className="rounded-full bg-gray-100 px-3 py-1 text-xs text-gray-600">{title.startYear}</span>
                        ) : null}
                        <span className={`rounded-full px-3 py-1 text-xs font-semibold ${releaseStatusColor(title.releaseStatusDimension)}`}>
                            {releaseStatusLabel(title.releaseStatusDimension)}
                        </span>
                    </div>

                    <EntryCounts title={title} />

                    {title.synopsis ? (
                        <SanitizedSynopsis html={title.synopsis} className="text-sm leading-relaxed text-gray-600" />
                    ) : null}

                    <EntryProviderStatus rawStatus={entry.rawStatus} rawListName={entry.rawListName} />
                </div>
            </div>
        </GlassCard>
    );
}
