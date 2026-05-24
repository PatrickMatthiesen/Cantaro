import { GlassCard } from '../../../ui';
import type { ReactNode } from 'react';

export interface MediaLibraryHeaderProps {
    totalCount: number;
    isLoading: boolean;
    isProviderConnected: boolean;
    formattedLastRemoteCheckAt: string | null;
    navigation?: ReactNode;
}

// fallow-ignore-next-line complexity
export function MediaLibraryHeader({
    totalCount,
    isLoading,
    isProviderConnected,
    formattedLastRemoteCheckAt,
    navigation,
}: MediaLibraryHeaderProps) {
    const details = [
        !isLoading && totalCount > 0
            ? { content: `${totalCount.toLocaleString()} entries`, className: 'mt-1 text-sm text-gray-500' }
            : null,
        isProviderConnected && formattedLastRemoteCheckAt
            ? { content: `Last checked AniList: ${formattedLastRemoteCheckAt}`, className: 'mt-1 text-xs text-gray-500' }
            : null,
    ].filter((detail): detail is { content: string; className: string } => detail !== null);
    return (
        <header className="flex flex-wrap items-center justify-between gap-3">
            <div>
                <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Media</p>
                <h1 className="mt-1 text-3xl font-bold">My Library</h1>
                {details.map((detail) => <p key={detail.content} className={detail.className}>{detail.content}</p>)}
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
