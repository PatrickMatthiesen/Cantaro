import { ActionButton } from '../../../ui';
import { Check, RefreshCw, Unplug } from 'lucide-react';
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
    isLoadingStatus: boolean;
    hasStatus: boolean;
    isConnected: boolean;
    isImporting: boolean;
    isDisconnecting: boolean;
    onConnect: () => void;
    onImport: () => Promise<MediaImportRequestDto | null>;
    onDisconnect: () => Promise<void>;
    onRetryStatus: () => Promise<void>;
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
        <div className="grid grid-cols-[3rem_minmax(0,1fr)] items-start gap-4 sm:grid-cols-[3rem_minmax(0,1fr)_auto]">
            <div className="flex h-12 w-12 shrink-0 items-center justify-center text-content" aria-hidden>{icon}</div>

            <div className="min-w-0 flex-1">
                <h2 className="text-xl font-bold text-content">{name}</h2>

                <p className="mt-1 max-w-2xl text-sm leading-6 text-content-muted">{description}</p>

                {status?.isConnected && status.displayName ? (
                    <p className="mt-3 text-sm text-content-muted">
                        Account <span className="font-semibold text-content">{status.displayName}</span>
                    </p>
                ) : null}
            </div>

            <div className="col-start-2 sm:col-start-auto sm:text-right">
                {isLoadingStatus ? (
                    <span className="inline-flex items-center gap-2 text-sm text-content-muted">
                        <RefreshCw className="h-4 w-4 animate-spin" aria-hidden /> Checking connection…
                    </span>
                ) : status?.isConnected ? (
                    <span className="inline-flex items-center gap-2 text-sm font-semibold text-success-content">
                        <Check className="h-4 w-4" aria-hidden /> Connected
                    </span>
                ) : status ? (
                    <span className="text-sm font-semibold text-content-muted">Not connected</span>
                ) : null}
            </div>
        </div>
    );
}

export function ProviderPanelError({ error }: { error: string | null }) {
    if (!error) {
        return null;
    }

    return (
        <div className="mt-6 border-l-2 border-danger-border pl-4 text-sm text-danger-content" role="alert">
            {error}
        </div>
    );
}

export function ProviderPanelImportSummary({ lastImport }: { lastImport: MediaImportDto | null }) {
    if (!lastImport) {
        return null;
    }

    return (
        <div className="mt-6 border-y border-success-border py-4 sm:ml-16" role="status" aria-live="polite">
            <p className="flex items-center gap-2 text-sm font-semibold text-success-content">
                <Check className="h-4 w-4" aria-hidden /> Last refresh
            </p>
            <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-3 text-sm sm:grid-cols-4">
                <div><dt className="text-content-muted">Imported</dt><dd className="mt-0.5 font-bold text-content">{lastImport.importedCount}</dd></div>
                <div><dt className="text-content-muted">New titles</dt><dd className="mt-0.5 font-bold text-content">{lastImport.createdTitles}</dd></div>
                <div><dt className="text-content-muted">Added</dt><dd className="mt-0.5 font-bold text-content">{lastImport.createdEntries}</dd></div>
                <div><dt className="text-content-muted">Updated</dt><dd className="mt-0.5 font-bold text-content">{lastImport.updatedEntries}</dd></div>
            </dl>
        </div>
    );
}

export function ProviderPanelActions({
    name,
    isLoadingStatus,
    hasStatus,
    isConnected,
    isImporting,
    isDisconnecting,
    onConnect,
    onImport,
    onDisconnect,
    onRetryStatus,
}: ProviderPanelActionsProps) {
    if (isLoadingStatus) {
        return null;
    }

    if (!hasStatus) {
        return (
            <ActionButton tone="secondary" onClick={() => void onRetryStatus()}>
                <RefreshCw className="h-4 w-4" aria-hidden /> Retry
            </ActionButton>
        );
    }

    if (!isConnected) {
        return (
            <ActionButton tone="personal" onClick={onConnect}>
                Connect {name}
            </ActionButton>
        );
    }

    return (
        <>
            <ActionButton
                tone="personal"
                onClick={() => void onImport()}
                disabled={isImporting || isDisconnecting}
                aria-busy={isImporting}
                busyLabel="Refreshing library…"
            >
                <RefreshCw className="h-4 w-4" aria-hidden /> Refresh library
            </ActionButton>
            <ActionButton
                tone="ghost"
                className="hover:bg-danger-surface hover:text-danger-content"
                onClick={() => void onDisconnect()}
                disabled={isDisconnecting || isImporting}
                aria-busy={isDisconnecting}
                busyLabel="Disconnecting…"
            >
                <Unplug className="h-4 w-4" aria-hidden /> Disconnect
            </ActionButton>
        </>
    );
}
