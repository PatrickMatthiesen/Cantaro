import { GlassCard, GradientButton } from '../ui/GlassComponents';

export interface MediaLibraryHeaderProps {
    totalCount: number;
    isLoading: boolean;
    isProviderConnected: boolean;
    formattedLastRemoteCheckAt: string | null;
    onNavigateProviders?: () => void;
    onNavigateHome?: () => void;
}

// fallow-ignore-next-line complexity
export function MediaLibraryHeader({
    totalCount,
    isLoading,
    isProviderConnected,
    formattedLastRemoteCheckAt,
    onNavigateProviders,
    onNavigateHome,
}: MediaLibraryHeaderProps) {
    const details = [
        !isLoading && totalCount > 0
            ? { content: `${totalCount.toLocaleString()} entries`, className: 'mt-1 text-sm text-gray-500' }
            : null,
        isProviderConnected && formattedLastRemoteCheckAt
            ? { content: `Last checked AniList: ${formattedLastRemoteCheckAt}`, className: 'mt-1 text-xs text-gray-500' }
            : null,
    ].filter((detail): detail is { content: string; className: string } => detail !== null);
    const actions = [
        onNavigateProviders ? { label: 'Providers', onClick: onNavigateProviders } : null,
        onNavigateHome ? { label: '← Home', onClick: onNavigateHome } : null,
    ].filter((action): action is { label: string; onClick: () => void } => action !== null);

    return (
        <header className="flex flex-wrap items-center justify-between gap-3">
            <div>
                <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Media</p>
                <h1 className="mt-1 text-3xl font-bold">My Library</h1>
                {details.map((detail) => <p key={detail.content} className={detail.className}>{detail.content}</p>)}
            </div>

            {actions.length > 0 ? (
                <div className="flex gap-2">
                    {actions.map((action) => <GradientButton key={action.label} tone="soft" onClick={action.onClick}>{action.label}</GradientButton>)}
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
