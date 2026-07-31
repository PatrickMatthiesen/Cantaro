import { LibraryEntryCard } from '../LibraryEntryCard';
import { GlassCard, GradientButton } from '../../../ui';
import type { MediaLibraryDensity } from '../../pages/MediaLibraryPage';
import type { MediaLibraryListItemDto, MediaLibraryQueryParams } from '../../services/mediaApi';

export interface LibraryContentSectionProps {
    error: string | null;
    isLoading: boolean;
    items: MediaLibraryListItemDto[];
    filters: MediaLibraryQueryParams;
    hasActiveFilters: boolean;
    totalPages: number;
    onRetry: () => Promise<void>;
    onNavigateEntry: (id: string) => void;
    onNavigateProviders?: () => void;
    onPreviousPage: () => void;
    onNextPage: () => void;
    density?: MediaLibraryDensity;
}

function libraryGridClassName(density: MediaLibraryDensity) {
    return density === 'compact'
        ? 'grid grid-cols-[repeat(auto-fill,minmax(min(7rem,100%),1fr))] gap-2'
        : 'grid grid-cols-[repeat(auto-fill,minmax(min(9rem,100%),1fr))] gap-3 sm:grid-cols-[repeat(auto-fill,minmax(10.5rem,1fr))] md:grid-cols-[repeat(auto-fill,minmax(14rem,1fr))]';
}

function LibraryLoadingGrid({ density }: { density: MediaLibraryDensity }) {
    const skeletonCount = density === 'compact' ? 9 : 10;

    return (
        <div className={libraryGridClassName(density)}>
            {Array.from({ length: skeletonCount }).map((_, index) => (
                <div
                    key={index}
                    className={`aspect-[0.72] animate-pulse overflow-hidden bg-surface-subtle shadow-[0_18px_45px_rgba(15,23,42,0.10)] ${density === 'compact' ? 'rounded-2xl' : 'rounded-[1.75rem]'}`}
                >
                    <div className="h-full w-full bg-surface-hover" />
                </div>
            ))}
        </div>
    );
}

function LibraryErrorState({ error, onRetry }: { error: string; onRetry: () => Promise<void> }) {
    return (
        <GlassCard className="p-6">
            <p className="text-sm text-danger-content">{error}</p>
            <div className="mt-3">
                <GradientButton tone="soft" onClick={() => void onRetry()}>Retry</GradientButton>
            </div>
        </GlassCard>
    );
}

function LibraryEmptyState({ hasActiveFilters, onNavigateProviders }: { hasActiveFilters: boolean; onNavigateProviders?: () => void }) {
    return (
        <GlassCard className="p-8 text-center">
            <p className="text-content-muted">No library entries found.</p>
            <p className="mt-1 text-sm text-content-subtle">
                {hasActiveFilters
                    ? 'Try removing some filters.'
                    : 'Import your library from a provider to get started.'}
            </p>
            {!hasActiveFilters && onNavigateProviders ? (
                <div className="mt-4">
                    <GradientButton gradient="from-blue-500 to-cyan-500" onClick={onNavigateProviders}>
                        Go to Providers
                    </GradientButton>
                </div>
            ) : null}
        </GlassCard>
    );
}

function LibraryPagination({ filters, totalPages, onPreviousPage, onNextPage }: { filters: MediaLibraryQueryParams; totalPages: number; onPreviousPage: () => void; onNextPage: () => void }) {
    if (totalPages <= 1) {
        return null;
    }

    return (
        <div className="flex items-center justify-center gap-2">
            <GradientButton tone="soft" disabled={filters.page === 1} onClick={onPreviousPage}>
                ← Previous
            </GradientButton>
            <span className="text-sm text-content-muted">
                Page {filters.page ?? 1} of {totalPages}
            </span>
            <GradientButton tone="soft" disabled={(filters.page ?? 1) >= totalPages} onClick={onNextPage}>
                Next →
            </GradientButton>
        </div>
    );
}

export function LibraryContentSection({
    error,
    isLoading,
    items,
    filters,
    hasActiveFilters,
    totalPages,
    onRetry,
    onNavigateEntry,
    onNavigateProviders,
    onPreviousPage,
    onNextPage,
    density = 'comfortable',
}: LibraryContentSectionProps) {
    if (error) {
        return <LibraryErrorState error={error} onRetry={onRetry} />;
    }

    if (isLoading) {
        return <LibraryLoadingGrid density={density} />;
    }

    if (items.length === 0) {
        return <LibraryEmptyState hasActiveFilters={hasActiveFilters} onNavigateProviders={onNavigateProviders} />;
    }

    return (
        <>
            <div className={libraryGridClassName(density)}>
                {items.map((entry) => (
                    <LibraryEntryCard
                        key={entry.id}
                        entry={entry}
                        density={density}
                        onClick={() => onNavigateEntry(entry.id)}
                    />
                ))}
            </div>

            <LibraryPagination
                filters={filters}
                totalPages={totalPages}
                onPreviousPage={onPreviousPage}
                onNextPage={onNextPage}
            />
        </>
    );
}
