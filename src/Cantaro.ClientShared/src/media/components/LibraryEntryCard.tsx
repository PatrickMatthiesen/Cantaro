import { useState } from 'react';
import { ImageIcon } from 'lucide-react';
import {
    LibraryEntryCardBadges,
    LibraryEntryCardDetails,
} from './media-library/LibraryEntryCardSections';
import { progressSegments, progressText } from './media-library/libraryEntryProgress';
import { formatRelativeReleaseTime } from '../services/mediaFormatting';
import type { MediaLibraryDensity } from '../pages/MediaLibraryPage';
import type { MediaLibraryListItemDto } from '../services/mediaApi';
import { MediaProviderIcon } from './MediaProviderIcon';
import { mediaProviderIconId } from '../services/mediaProviders';

export interface LibraryEntryCardProps {
    entry: MediaLibraryListItemDto;
    onClick: () => void;
    density?: MediaLibraryDensity;
    collection?: string;
}

function LibraryArtwork({ posterUrl, title, provider }: { posterUrl?: string; title: string; provider?: string }) {
    const [failed, setFailed] = useState(false);
    const iconId = mediaProviderIconId(provider);

    if (!posterUrl || failed) {
        return (
            <div className="flex h-full w-full items-center justify-center bg-surface-subtle">
                {iconId ? <MediaProviderIcon providerId={iconId} className="h-8 w-8" aria-hidden /> : <ImageIcon className="h-8 w-8 text-content-muted" aria-hidden />}
                <span className="sr-only">{title} — no artwork available</span>
            </div>
        );
    }

    return (
        <img
            src={posterUrl}
            alt={`${title} cover art`}
            loading="lazy"
            decoding="async"
            className="h-full w-full object-cover transition duration-300 group-hover:scale-[1.025] group-hover:saturate-125 motion-reduce:transition-none"
            onError={() => setFailed(true)}
        />
    );
}

export function LibraryEntryCard({ entry, onClick, density = 'comfortable', collection }: LibraryEntryCardProps) {
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
            aria-label={`Open ${entry.canonicalTitle}`}
            className="group h-full w-full border border-border-subtle bg-surface-subtle text-left transition-colors hover:border-border-strong focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
            <div className="relative aspect-[0.72] overflow-hidden bg-surface-subtle">
                <LibraryArtwork posterUrl={entry.posterUrl} title={entry.canonicalTitle} provider={entry.provider} />

                <LibraryEntryCardBadges
                    topLeftBadge={topLeftBadge}
                    progress={progress}
                    isConnected={entry.isConnected}
                    density={density}
                />

                <LibraryEntryCardDetails
                    collection={collection}
                    entry={entry}
                    progress={progressBar}
                    density={density}
                />
            </div>
        </button>
    );
}
