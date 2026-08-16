import { LibraryEntryCard } from '../LibraryEntryCard';
import { ActionButton } from '../../../ui';
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
        ? 'grid grid-cols-3 gap-x-2 gap-y-4 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 xl:grid-cols-7 2xl:grid-cols-8'
        : 'grid grid-cols-2 gap-x-3 gap-y-5 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 2xl:grid-cols-6';
}

function LibraryLoadingGrid({ density }: { density: MediaLibraryDensity }) {
    const skeletonCount = density === 'compact' ? 9 : 10;

    return (
        <div className={libraryGridClassName(density)}>
            {Array.from({ length: skeletonCount }).map((_, index) => (
                <div
                    key={index}
                    className="aspect-[0.72] animate-pulse border border-border-subtle bg-surface-subtle"
                />
            ))}
        </div>
    );
}

function LibraryErrorState({ onRetry }: { onRetry: () => Promise<void> }) {
    return (
        <section className="border-y border-danger-border bg-danger-surface px-4 py-6">
            <p className="font-semibold text-danger-content">Cantaro couldn’t load your saved library.</p>
            <p className="mt-1 text-sm text-content-muted">Your library data is still stored in Cantaro. Try loading it again.</p>
            <div className="mt-3">
                <ActionButton tone="secondary" onClick={() => void onRetry()}>Try again</ActionButton>
            </div>
        </section>
    );
}

function LibraryEmptyState({ hasActiveFilters, onNavigateProviders }: { hasActiveFilters: boolean; onNavigateProviders?: () => void }) {
    return (
        <section className="border-y border-border-subtle py-12 text-center">
            <p className="font-semibold text-content">No library entries found</p>
            <p className="mt-1 text-sm text-content-subtle">
                {hasActiveFilters
                    ? 'Try removing some filters.'
                    : 'Import your library from a provider to get started.'}
            </p>
            {!hasActiveFilters && onNavigateProviders ? (
                <div className="mt-4">
                    <ActionButton tone="personal" onClick={onNavigateProviders}>
                        Go to Providers
                    </ActionButton>
                </div>
            ) : null}
        </section>
    );
}

function LibraryPagination({ filters, totalPages, onPreviousPage, onNextPage }: { filters: MediaLibraryQueryParams; totalPages: number; onPreviousPage: () => void; onNextPage: () => void }) {
    if (totalPages <= 1) {
        return null;
    }

    return (
        <nav className="flex flex-wrap items-center justify-center gap-2 border-t border-border-subtle pt-6" aria-label="Library pages">
            <ActionButton tone="ghost" disabled={filters.page === 1} onClick={onPreviousPage}>
                ← Previous
            </ActionButton>
            <span className="text-sm text-content-muted">
                Page {filters.page ?? 1} of {totalPages}
            </span>
            <ActionButton tone="ghost" disabled={(filters.page ?? 1) >= totalPages} onClick={onNextPage}>
                Next →
            </ActionButton>
        </nav>
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
        return <LibraryErrorState onRetry={onRetry} />;
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
                        onClick={() => onNavigateEntry(entry.mediaTitleId)}
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
