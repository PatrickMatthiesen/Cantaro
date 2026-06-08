import { GlassCard } from '../../../ui';
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
                <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Media</p>
                <h1 className="mt-1 text-3xl font-bold">My Library</h1>
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
        <GlassCard className="p-4">
            <p className="text-sm text-rose-700">{error}</p>
        </GlassCard>
    );
}
