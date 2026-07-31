import { GradientButton, StatusBadge } from '../../../ui';
import type { ReactNode } from 'react';
import type { MediaImportDto, MediaImportRequestDto, MediaProviderAccountStatusDto } from '../../services/mediaApi';

interface ProviderPanelHeaderProps {
    name: string;
    icon: ReactNode;
    description: string;
    status: MediaProviderAccountStatusDto | null;
    isLoadingStatus: boolean;
}

interface ProviderPanelActionsProps {
    name: string;
    gradient: string;
    isLoadingStatus: boolean;
    isConnected: boolean;
    isImporting: boolean;
    isDisconnecting: boolean;
    onConnect: () => void;
    onImport: () => Promise<MediaImportRequestDto | null>;
    onDisconnect: () => Promise<void>;
}

// fallow-ignore-next-line complexity
export function ProviderPanelHeader({
    name,
    icon,
    description,
    status,
    isLoadingStatus,
}: ProviderPanelHeaderProps) {
    return (
        <div className="flex items-start gap-4">
            <div className="flex h-14 w-14 shrink-0 items-center justify-center" aria-hidden>{icon}</div>

            <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-3">
                    <h3 className="text-xl font-semibold text-content">{name}</h3>
                    {!isLoadingStatus && status ? (
                        <StatusBadge status={status.isConnected ? 'connected' : 'available'} />
                    ) : null}
                </div>

                <p className="mt-1 text-sm text-content-muted">{description}</p>

                {status?.isConnected && status.displayName ? (
                    <p className="mt-1 text-xs text-content-muted">
                        Signed in as <span className="font-medium text-content">{status.displayName}</span>
                    </p>
                ) : null}
            </div>
        </div>
    );
}

export function ProviderPanelError({ error }: { error: string | null }) {
    if (!error) {
        return null;
    }

    return <div className="mt-4 rounded-2xl bg-danger-surface px-4 py-3 text-sm text-danger-content">{error}</div>;
}

export function ProviderPanelImportSummary({ lastImport }: { lastImport: MediaImportDto | null }) {
    if (!lastImport) {
        return null;
    }

    return (
        <div className="mt-4 rounded-2xl bg-success-surface px-4 py-3 text-sm text-success-content">
            <p className="font-semibold">Import complete</p>
            <ul className="mt-1 space-y-0.5 text-xs text-success-content">
                <li>{lastImport.importedCount} entries imported</li>
                <li>{lastImport.createdTitles} new titles created</li>
                <li>
                    {lastImport.createdEntries} entries added · {lastImport.updatedEntries} entries updated
                </li>
            </ul>
        </div>
    );
}

export function ProviderPanelActions({
    name,
    gradient,
    isLoadingStatus,
    isConnected,
    isImporting,
    isDisconnecting,
    onConnect,
    onImport,
    onDisconnect,
}: ProviderPanelActionsProps) {
    if (isLoadingStatus) {
        return <span className="text-xs text-content-muted">Checking connection…</span>;
    }

    if (!isConnected) {
        return (
            <GradientButton gradient={gradient} onClick={onConnect}>
                Connect {name}
            </GradientButton>
        );
    }

    return (
        <>
            <GradientButton gradient={gradient} onClick={() => void onImport()} disabled={isImporting} aria-busy={isImporting}>
                {isImporting ? 'Refreshing…' : 'Refresh library'}
            </GradientButton>
            <GradientButton tone="soft" onClick={() => void onDisconnect()} disabled={isDisconnecting} aria-busy={isDisconnecting}>
                {isDisconnecting ? 'Disconnecting…' : 'Disconnect'}
            </GradientButton>
        </>
    );
}
