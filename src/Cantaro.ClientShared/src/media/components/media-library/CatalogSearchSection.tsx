import { useState } from 'react';
import { ActionButton } from '../../../ui';
import { mediaKindLabel } from '../../services/mediaFormatting';
import type { MediaProviderSearchResultDto } from '../../services/mediaApi';

const CATALOG_RESULTS_GRID_CLASS_NAME = 'grid grid-cols-2 gap-x-3 gap-y-5 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 2xl:grid-cols-6';

export interface CatalogSearchSectionProps {
    providerName: string;
    query: string;
    results: MediaProviderSearchResultDto[];
    isLoading: boolean;
    error: string | null;
    hasSearched: boolean;
    onRetry: () => void;
    onNavigateCatalogResult: (providerId: string, providerMediaId: string) => void;
    onNavigateEntry: (mediaTitleId: string) => void;
}

function CatalogPoster({ posterUrl, title }: { posterUrl?: string; title: string }) {
    const [failed, setFailed] = useState(false);

    if (!posterUrl || failed) {
        return (
            <div className="flex h-full w-full items-center justify-center bg-surface-subtle text-xs font-semibold text-content-subtle">
                No art
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

function catalogResultMetadata(result: MediaProviderSearchResultDto): string[] {
    const metadata: string[] = [];
    if (result.startYear) metadata.push(String(result.startYear));
    if (result.episodeCount) metadata.push(`${result.episodeCount} ep`);
    if (result.chapterCount) metadata.push(`${result.chapterCount} ch`);
    return metadata;
}

function CatalogResultBadges({ mediaKind, isInLibrary }: { mediaKind: string; isInLibrary: boolean }) {
    return (
        <div className="absolute inset-x-0 top-0 flex items-center justify-between gap-2 bg-linear-to-b from-black/78 to-transparent p-3 pb-9">
            <span className="text-xs font-semibold text-white">
                {mediaKindLabel(mediaKind)}
            </span>
            {isInLibrary ? (
                <span className="text-xs font-semibold text-emerald-300">
                    In library
                </span>
            ) : null}
        </div>
    );
}

function CatalogResultText({ result, metadata }: { result: MediaProviderSearchResultDto; metadata: string[] }) {
    return (
        <div className="absolute right-3 bottom-3 left-3">
            <h3 className="line-clamp-2 text-base leading-tight font-semibold text-white sm:text-lg">{result.title}</h3>
            {result.nativeTitle ? <p className="mt-1 truncate text-xs text-white/70">{result.nativeTitle}</p> : null}
            {metadata.length > 0 ? (
                <p className="mt-2 text-xs font-medium text-white/70">{metadata.join(' / ')}</p>
            ) : null}
        </div>
    );
}

function CatalogLibraryAction({
    mediaTitleId,
    onNavigateEntry,
}: {
    mediaTitleId?: string;
    onNavigateEntry: (mediaTitleId: string) => void;
}) {
    if (!mediaTitleId) return null;

    return (
        <div className="border-t border-border-subtle">
            <ActionButton
                tone="ghost"
                fullWidth
                onClick={() => onNavigateEntry(mediaTitleId)}
            >
                Open library entry
            </ActionButton>
        </div>
    );
}

function CatalogResultCard({
    result,
    onNavigateCatalogResult,
    onNavigateEntry,
}: {
    result: MediaProviderSearchResultDto;
    onNavigateCatalogResult: (providerId: string, providerMediaId: string) => void;
    onNavigateEntry: (mediaTitleId: string) => void;
}) {
    const mediaTitleId = result.libraryState?.mediaTitleId;
    const isInLibrary = Boolean(result.libraryState?.isInLibrary && mediaTitleId);
    const metadata = catalogResultMetadata(result);

    return (
        <article className="group overflow-hidden border border-border-subtle bg-surface transition-colors hover:border-border-strong">
            <button
                type="button"
                className="block w-full text-left focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-focus"
                onClick={() => onNavigateCatalogResult(result.providerId, result.providerMediaId)}
                aria-label={`Open ${result.title} from ${result.providerId}`}
            >
                <div className="relative aspect-[0.72] overflow-hidden">
                    <CatalogPoster posterUrl={result.posterUrl} title={result.title} />
                    <div className="absolute inset-0 bg-linear-to-t from-slate-950 via-slate-900/30 to-transparent" aria-hidden />
                    <CatalogResultBadges mediaKind={result.mediaKind} isInLibrary={isInLibrary} />
                    <CatalogResultText result={result} metadata={metadata} />
                </div>
            </button>
            <CatalogLibraryAction mediaTitleId={isInLibrary ? mediaTitleId : undefined} onNavigateEntry={onNavigateEntry} />
        </article>
    );
}

export function CatalogSearchSection({
    providerName,
    query,
    results,
    isLoading,
    error,
    hasSearched,
    onRetry,
    onNavigateCatalogResult,
    onNavigateEntry,
}: CatalogSearchSectionProps) {
    if (error) {
        return (
            <section className="border-y border-danger-border bg-danger-surface px-4 py-6">
                <p className="text-sm text-danger-content">{error}</p>
                <div className="mt-3">
                    <ActionButton tone="secondary" onClick={onRetry}>Retry</ActionButton>
                </div>
            </section>
        );
    }

    if (isLoading) {
        return (
            <div className={CATALOG_RESULTS_GRID_CLASS_NAME}>
                {Array.from({ length: 10 }).map((_, index) => (
                    <div key={index} className="aspect-[0.72] animate-pulse border border-border-subtle bg-surface-subtle" />
                ))}
            </div>
        );
    }

    if (!hasSearched) {
        return (
            <section className="border-y border-border-subtle py-12 text-center">
                <p className="font-medium text-content-muted">Search {providerName} for something new to watch or read.</p>
            </section>
        );
    }

    if (results.length === 0) {
        return (
            <section className="border-y border-border-subtle py-12 text-center">
                <p className="text-content-muted">No {providerName} results found for "{query}".</p>
            </section>
        );
    }

    return (
        <div className={CATALOG_RESULTS_GRID_CLASS_NAME}>
            {results.map((result) => (
                <CatalogResultCard
                    key={`${result.providerId}:${result.providerMediaId}`}
                    result={result}
                    onNavigateCatalogResult={onNavigateCatalogResult}
                    onNavigateEntry={onNavigateEntry}
                />
            ))}
        </div>
    );
}
