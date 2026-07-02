import { GlassCard } from '../../../ui';
import { DetailArtwork, SanitizedSynopsis } from './EntryDisplayPrimitives';
import { formatNextReleaseDisplay } from '../../services/mediaFormatting';
import type { MediaLibraryEntryDetailDto } from '../../services/mediaApi';

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

function EntryReleaseBadge({
    label,
    nextRelease,
}: {
    label?: string;
    nextRelease: NonNullable<ReturnType<typeof formatNextReleaseDisplay>>;
}) {
    return (
        <div className="shrink-0 self-start rounded-2xl border border-white/18 bg-white/14 px-4 py-3 text-right shadow-sm backdrop-blur-md">
            <p className="text-[11px] font-semibold tracking-[0.18em] text-cyan-100 uppercase">
                {label ?? 'Next release'}
            </p>
            <p className="mt-1 text-sm font-semibold text-white">{nextRelease.relative}</p>
            <p className="mt-1 text-xs text-white/62">{nextRelease.absolute}</p>
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
                ? counts.map((count) => <span key={count} className="text-sm text-white/78">{count}</span>)
                : <span className="text-sm text-white/52 italic">Count unknown</span>}
        </div>
    );
}

function EntryProviderStatus({ rawStatus, rawListName }: { rawStatus?: string | null; rawListName?: string | null }) {
    if (!rawStatus && !rawListName) {
        return null;
    }

    return <p className="text-xs text-white/50">Provider status: {rawListName ?? rawStatus}</p>;
}

export interface EntryOverviewCardProps {
    entry: MediaLibraryEntryDetailDto;
    nextRelease: ReturnType<typeof formatNextReleaseDisplay>;
}

function EntryAmbientBackdrop({ posterUrl }: { posterUrl?: string | null }) {
    if (!posterUrl) {
        return null;
    }

    return (
        <div className="pointer-events-none absolute inset-0" aria-hidden>
            <img
                src={posterUrl}
                alt=""
                className="h-full w-full scale-105 object-cover opacity-70 blur-sm"
            />
            <div className="absolute inset-0 bg-slate-950/58" />
            <div className="absolute inset-0 bg-linear-to-t from-slate-950 via-slate-950/78 to-slate-950/28" />
            <div className="absolute inset-0 bg-linear-to-r from-slate-950/60 via-slate-950/18 to-slate-950/46" />
        </div>
    );
}

// fallow-ignore-next-line complexity
export function EntryOverviewCard({ entry, nextRelease }: EntryOverviewCardProps) {
    const { title } = entry;

    return (
        <GlassCard className="overflow-hidden border-white/18 bg-slate-950 text-white shadow-[0_24px_70px_rgba(15,23,42,0.22)]">
            <EntryAmbientBackdrop posterUrl={title.posterUrl} />
            <div className="relative z-10 flex flex-col gap-5 p-5 sm:flex-row sm:gap-6 sm:p-6">
                <div className="h-48 w-32 shrink-0 overflow-hidden rounded-2xl shadow-[0_18px_46px_rgba(15,23,42,0.24)] ring-1 ring-white/70 sm:h-56 sm:w-40">
                    <DetailArtwork posterUrl={title.posterUrl} title={title.canonicalTitle} />
                </div>

                <div className="min-w-0 flex-1 space-y-3">
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                        <div className="min-w-0">
                            <h1 className="text-2xl leading-tight font-bold text-white">{title.canonicalTitle}</h1>
                            {title.originalTitle && title.originalTitle !== title.canonicalTitle ? (
                                <p className="mt-1 text-sm text-white/68">{title.originalTitle}</p>
                            ) : null}
                        </div>

                        {nextRelease ? <EntryReleaseBadge label={entry.nextReleaseLabel ?? undefined} nextRelease={nextRelease} /> : null}
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                        {title.startYear ? (
                            <span className="rounded-full bg-white/14 px-3 py-1 text-xs font-semibold text-white/78 backdrop-blur-md">{title.startYear}</span>
                        ) : null}
                        <span className="rounded-full bg-white/14 px-3 py-1 text-xs font-semibold text-white/78 backdrop-blur-md">
                            {releaseStatusLabel(title.releaseStatusDimension)}
                        </span>
                    </div>

                    <EntryCounts title={title} />

                    {title.synopsis ? (
                        <SanitizedSynopsis html={title.synopsis} className="text-sm leading-relaxed text-white/78" />
                    ) : null}

                    <EntryProviderStatus rawStatus={entry.rawStatus} rawListName={entry.rawListName} />
                </div>
            </div>
        </GlassCard>
    );
}
