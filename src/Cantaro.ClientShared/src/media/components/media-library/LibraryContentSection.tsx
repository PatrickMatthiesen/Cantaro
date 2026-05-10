import { LibraryEntryCard } from '../LibraryEntryCard';
import { GlassCard, GradientButton } from '../../../ui';
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
}

function LibraryLoadingGrid() {
    return (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
            {Array.from({ length: 10 }).map((_, index) => (
                <GlassCard key={index} className="aspect-[0.72] animate-pulse bg-white/50" />
            ))}
        </div>
    );
}

function LibraryErrorState({ error, onRetry }: { error: string; onRetry: () => Promise<void> }) {
    return (
        <GlassCard className="p-6">
            <p className="text-sm text-rose-700">{error}</p>
            <div className="mt-3">
                <GradientButton tone="soft" onClick={() => void onRetry()}>Retry</GradientButton>
            </div>
        </GlassCard>
    );
}

function LibraryEmptyState({ hasActiveFilters, onNavigateProviders }: { hasActiveFilters: boolean; onNavigateProviders?: () => void }) {
    return (
        <GlassCard className="p-8 text-center">
            <p className="text-gray-500">No library entries found.</p>
            <p className="mt-1 text-sm text-gray-400">
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
            <span className="text-sm text-gray-600">
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
}: LibraryContentSectionProps) {
    if (error) {
        return <LibraryErrorState error={error} onRetry={onRetry} />;
    }

    if (isLoading) {
        return <LibraryLoadingGrid />;
    }

    if (items.length === 0) {
        return <LibraryEmptyState hasActiveFilters={hasActiveFilters} onNavigateProviders={onNavigateProviders} />;
    }

    return (
        <>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
                {items.map((entry) => (
                    <LibraryEntryCard key={entry.id} entry={entry} onClick={() => onNavigateEntry(entry.id)} />
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
