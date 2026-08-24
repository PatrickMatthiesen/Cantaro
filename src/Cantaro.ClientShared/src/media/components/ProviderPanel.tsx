import type { ReactNode } from 'react';
import {
    ProviderPanelActions,
    ProviderPanelError,
    ProviderPanelHeader,
    ProviderPanelImportSummary,
    ProviderPanelInitialSync,
} from './media-providers/ProviderPanelSections';
import { useProviderPanelState } from './media-providers/useProviderPanelState';

export interface ProviderPanelProps {
    providerId: string;
    name: string;
    icon: ReactNode;
    description: string;
}

type ProviderPanelState = ReturnType<typeof useProviderPanelState>;

function InitialSyncPanelSlot({
    name,
    state,
    isActive,
}: {
    name: string;
    state: ProviderPanelState;
    isActive: boolean;
}) {
    if (!isActive) {
        return null;
    }

    return (
        <ProviderPanelInitialSync
            name={name}
            preview={state.initialSyncPreview}
            result={state.initialSyncResult}
            isPreviewing={state.isPreviewingInitialSync}
            isApplying={state.isApplyingInitialSync}
            onApply={state.handleApplyInitialSync}
            onRetry={state.handlePreviewInitialSync}
            onImport={state.handleImport}
            onDismiss={state.handleDismissInitialSync}
        />
    );
}

function confirmProviderDisconnect(name: string, disconnect: () => Promise<void>): Promise<void> {
    if (!window.confirm(`Disconnect ${name}? Your saved Cantaro library will remain available.`)) {
        return Promise.resolve();
    }

    return disconnect();
}

export function ProviderPanel({ providerId, name, icon, description }: ProviderPanelProps) {
    const state = useProviderPanelState(providerId);
    const {
        status,
        isLoadingStatus,
        error,
        isImporting,
        isDisconnecting,
        lastImport,
        initialSyncPreview,
        initialSyncResult,
        isPreviewingInitialSync,
        isApplyingInitialSync,
        handleConnect,
        handleDisconnect,
        handleImport,
        handlePreviewInitialSync,
        reloadStatus,
    } = state;
    const isInitialSyncActive = [
        initialSyncPreview,
        initialSyncResult,
        isPreviewingInitialSync,
        isApplyingInitialSync,
    ].some(Boolean);

    return (
        <article className="py-7 sm:py-9">
            <ProviderPanelHeader
                name={name}
                icon={icon}
                description={description}
                status={status}
                isLoadingStatus={isLoadingStatus}
            />

            <ProviderPanelError error={error} />
            <InitialSyncPanelSlot name={name} state={state} isActive={isInitialSyncActive} />
            <ProviderPanelImportSummary lastImport={lastImport} />

            <div className="mt-6 flex flex-wrap gap-3 sm:pl-16">
                <ProviderPanelActions
                    name={name}
                    isLoadingStatus={isLoadingStatus}
                    hasStatus={status !== null}
                    isConnected={Boolean(status?.isConnected)}
                    onConnect={handleConnect}
                    onRetryStatus={reloadStatus}
                    connectedActions={{
                        isImporting,
                        isDisconnecting,
                        isInitialSyncActive,
                        onImport: handleImport,
                        onPreviewInitialSync: handlePreviewInitialSync,
                        onDisconnect: () => confirmProviderDisconnect(name, handleDisconnect),
                    }}
                />
            </div>
        </article>
    );
}
