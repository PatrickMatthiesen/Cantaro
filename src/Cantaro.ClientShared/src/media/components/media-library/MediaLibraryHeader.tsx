import type { ReactNode } from 'react';

export interface MediaLibraryHeaderProps {
    navigation?: ReactNode;
}

export function MediaLibraryHeader({
    navigation,
}: MediaLibraryHeaderProps) {
    return (
        <header className="flex flex-wrap items-center justify-between gap-3">
            <div>
                <p className="text-sm font-semibold text-content-muted">Media</p>
                <h1 className="mt-1 text-3xl font-black text-content">Library</h1>
            </div>

            {navigation ? (
                <div className="flex flex-wrap items-center gap-3">
                    {navigation}
                </div>
            ) : null}
        </header>
    );
}

export function MediaLibraryRefreshErrorNotice({ error }: { error: string | null }) {
    if (!error) {
        return null;
    }

    return (
        <div className="border-y border-danger-border bg-danger-surface px-4 py-3" role="status">
            <p className="text-sm font-medium text-danger-content">{error}</p>
        </div>
    );
}
