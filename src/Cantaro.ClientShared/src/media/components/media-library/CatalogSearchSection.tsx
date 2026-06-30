import { useState } from 'react';
import { GlassCard, GradientButton } from '../../../ui';
import { mediaKindLabel } from '../../services/mediaFormatting';
import type { MediaProviderSearchResultDto } from '../../services/mediaApi';

const CATALOG_RESULTS_GRID_CLASS_NAME = 'grid grid-cols-[repeat(auto-fill,minmax(min(9rem,100%),1fr))] gap-3 sm:grid-cols-[repeat(auto-fill,minmax(10.5rem,1fr))] sm:gap-4 lg:grid-cols-[repeat(auto-fill,minmax(11.5rem,1fr))]';

export interface CatalogSearchSectionProps {
    providerName: string;
    query: string;
    results: MediaProviderSearchResultDto[];
    isLoading: boolean;
    error: string | null;
    hasSearched: boolean;
    onRetry: () => void;
    onNavigateCatalogResult: (providerId: string, providerMediaId: string) => void;
    onNavigateEntry: (libraryEntryId: string) => void;
}

function CatalogPoster({ posterUrl, title }: { posterUrl?: string; title: string }) {
    const [failed, setFailed] = useState(false);

    if (!posterUrl || failed) {
        return (
            <div className="flex h-full w-full items-center justify-center bg-gray-100 text-xs font-semibold text-gray-400">
                No art
            </div>
        );
    }

    return (
        <img
            src={posterUrl}
            alt={`${title} cover art`}
            loading="lazy"
            className="h-full w-full object-cover"
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
        <div className="absolute top-3 left-3 flex flex-wrap gap-2">
            <span className="rounded-full bg-white/90 px-2.5 py-1 text-xs font-semibold text-gray-800">
                {mediaKindLabel(mediaKind)}
            </span>
            {isInLibrary ? (
                <span className="rounded-full bg-emerald-100/95 px-2.5 py-1 text-xs font-semibold text-emerald-800">
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
    libraryEntryId,
    onNavigateEntry,
}: {
    libraryEntryId?: string;
    onNavigateEntry: (libraryEntryId: string) => void;
}) {
    if (!libraryEntryId) return null;

    return (
        <div className="border-t border-white/70 bg-white/85 p-3">
            <GradientButton
                tone="soft"
                className="w-full justify-center"
                onClick={() => onNavigateEntry(libraryEntryId)}
            >
                Open library entry
            </GradientButton>
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
    onNavigateEntry: (libraryEntryId: string) => void;
}) {
    const libraryEntryId = result.libraryState?.libraryEntryId;
    const isInLibrary = Boolean(result.libraryState?.isInLibrary && libraryEntryId);
    const metadata = catalogResultMetadata(result);

    return (
        <GlassCard interactive className="overflow-hidden p-0">
            <button
                type="button"
                className="flex h-full w-full flex-col text-left"
                onClick={() => onNavigateCatalogResult(result.providerId, result.providerMediaId)}
            >
                <div className="relative aspect-[0.72] overflow-hidden md:min-h-76">
                    <CatalogPoster posterUrl={result.posterUrl} title={result.title} />
                    <div className="absolute inset-0 bg-linear-to-t from-slate-950 via-slate-900/30 to-transparent" aria-hidden />
                    <CatalogResultBadges mediaKind={result.mediaKind} isInLibrary={isInLibrary} />
                    <CatalogResultText result={result} metadata={metadata} />
                </div>
            </button>
            <CatalogLibraryAction libraryEntryId={isInLibrary ? libraryEntryId : undefined} onNavigateEntry={onNavigateEntry} />
        </GlassCard>
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
            <GlassCard className="p-6">
                <p className="text-sm text-rose-700">{error}</p>
                <div className="mt-3">
                    <GradientButton tone="soft" onClick={onRetry}>Retry</GradientButton>
                </div>
            </GlassCard>
        );
    }

    if (isLoading) {
        return (
            <div className={CATALOG_RESULTS_GRID_CLASS_NAME}>
                {Array.from({ length: 10 }).map((_, index) => (
                    <GlassCard key={index} className="aspect-[0.72] animate-pulse bg-white/50" />
                ))}
            </div>
        );
    }

    if (!hasSearched) {
        return (
            <GlassCard className="p-8 text-center">
                <p className="font-medium text-gray-600">Search {providerName} for something new to watch or read.</p>
            </GlassCard>
        );
    }

    if (results.length === 0) {
        return (
            <GlassCard className="p-8 text-center">
                <p className="text-gray-500">No {providerName} results found for "{query}".</p>
            </GlassCard>
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
