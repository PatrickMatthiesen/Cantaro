import { LibraryEntryCard } from '../LibraryEntryCard';
import { ActionButton } from '../../../ui';
import type { MediaLibraryDensity } from '../../pages/MediaLibraryPage';
import type { MediaApiError } from '../../services/mediaApi.errors';
import type { MediaLibraryListItemDto, MediaLibraryQueryParams } from '../../services/mediaApi';

export interface LibraryContentSectionProps {
    error: MediaApiError | null;
    isLoading: boolean;
    hasCurrentData: boolean;
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

const libraryErrorMessages: Partial<Record<MediaApiError['kind'], { title: string; detail: string }>> = {
    connection: {
        title: "Can't connect to the Cantaro server",
        detail: 'Check your connection, then try again.',
    },
    authentication: {
        title: 'Your Cantaro session has expired',
        detail: 'Sign in again, then try loading your library.',
    },
    authorization: {
        title: "Cantaro can't access your library",
        detail: 'Check your account permissions, then try again.',
    },
    'service-unavailable': {
        title: 'The Cantaro server is temporarily unavailable',
        detail: 'Try again in a moment.',
    },
    server: {
        title: "The Cantaro server couldn't load your library",
        detail: 'Try again in a moment.',
    },
};

function LibraryErrorState({ error, onRetry }: { error: MediaApiError; onRetry: () => Promise<void> }) {
    const { title, detail } = libraryErrorMessages[error.kind] ?? {
        title: "Cantaro couldn't load your library",
        detail: 'Try again to reload your library.',
    };

    return (
        <section className="border-y border-danger-border bg-danger-surface px-4 py-6">
            <p className="font-semibold text-danger-content">{title}</p>
            <p className="mt-1 text-sm text-danger-content">{detail}</p>
            <div className="mt-3">
                <ActionButton tone="secondary" onClick={() => void onRetry()}>Try again</ActionButton>
            </div>
        </section>
    );
}

function LibraryInlineError({ error, onRetry }: { error: MediaApiError; onRetry: () => Promise<void> }) {
    return (
        <div className="border-y border-danger-border bg-danger-surface px-4 py-3" role="status">
            <p className="text-sm font-medium text-danger-content">{error.message}</p>
            <div className="mt-2">
                <ActionButton tone="secondary" onClick={() => void onRetry()}>Try again</ActionButton>
            </div>
        </div>
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

function LibraryResults({
    items,
    filters,
    hasActiveFilters,
    totalPages,
    onNavigateEntry,
    onNavigateProviders,
    onPreviousPage,
    onNextPage,
    density,
}: Omit<LibraryContentSectionProps, 'error' | 'isLoading' | 'hasCurrentData' | 'onRetry' | 'density'> & { density: MediaLibraryDensity }) {
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
                        collection={filters.collection}
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

export function LibraryContentSection({
    error,
    isLoading,
    hasCurrentData,
    onRetry,
    density = 'comfortable',
    ...results
}: LibraryContentSectionProps) {
    if (!hasCurrentData) {
        if (error) return <LibraryErrorState error={error} onRetry={onRetry} />;
        if (isLoading) return <LibraryLoadingGrid density={density} />;
    }

    return (
        <>
            {error && <LibraryInlineError error={error} onRetry={onRetry} />}
            <LibraryResults {...results} density={density} />
        </>
    );
}
