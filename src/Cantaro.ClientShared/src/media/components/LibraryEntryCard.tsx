import { useState } from 'react';
import {
    LibraryEntryCardBadges,
    LibraryEntryCardDetails,
} from './media-library/LibraryEntryCardSections';
import { progressSegments, progressText } from './media-library/libraryEntryProgress';
import { formatRelativeReleaseTime } from '../services/mediaFormatting';
import type { MediaLibraryDensity } from '../pages/MediaLibraryPage';
import type { MediaLibraryListItemDto } from '../services/mediaApi';
import { MediaProviderIcon } from './MediaProviderIcon';

export interface LibraryEntryCardProps {
    entry: MediaLibraryListItemDto;
    onClick: () => void;
    density?: MediaLibraryDensity;
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
