import type { ReactNode } from 'react';
import {
    ProviderPanelActions,
    ProviderPanelError,
    ProviderPanelHeader,
    ProviderPanelImportSummary,
} from './media-providers/ProviderPanelSections';
import { useProviderPanelState } from './media-providers/useProviderPanelState';

export interface ProviderPanelProps {
    providerId: string;
    name: string;
    icon: ReactNode;
    description: string;
}

export function ProviderPanel({ providerId, name, icon, description }: ProviderPanelProps) {
    const {
        status,
        isLoadingStatus,
        error,
        isImporting,
        isDisconnecting,
        lastImport,
        handleConnect,
        handleDisconnect,
        handleImport,
        reloadStatus,
    } = useProviderPanelState(providerId);

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
            <ProviderPanelImportSummary lastImport={lastImport} />

            <div className="mt-6 flex flex-wrap gap-3 sm:pl-16">
                <ProviderPanelActions
                    name={name}
                    isLoadingStatus={isLoadingStatus}
                    hasStatus={status !== null}
                    isConnected={Boolean(status?.isConnected)}
                    isImporting={isImporting}
                    isDisconnecting={isDisconnecting}
                    onConnect={handleConnect}
                    onImport={handleImport}
                    onDisconnect={() => {
                        if (!window.confirm(`Disconnect ${name}? Your saved Cantaro library will remain available.`)) {
                            return Promise.resolve();
                        }

                        return handleDisconnect();
                    }}
                    onRetryStatus={reloadStatus}
                />
            </div>
        </article>
    );
}
