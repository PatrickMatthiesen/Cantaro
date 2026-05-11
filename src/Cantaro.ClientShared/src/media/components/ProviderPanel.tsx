import { GlassCard } from '../../ui';
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
    icon: string;
    gradient: string;
    description: string;
}

export function ProviderPanel({ providerId, name, icon, gradient, description }: ProviderPanelProps) {
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
    } = useProviderPanelState(providerId);

    return (
        <GlassCard className="p-6">
            <ProviderPanelHeader
                name={name}
                icon={icon}
                gradient={gradient}
                description={description}
                status={status}
                isLoadingStatus={isLoadingStatus}
            />

            <ProviderPanelError error={error} />
            <ProviderPanelImportSummary lastImport={lastImport} />

            <div className="mt-5 flex flex-wrap gap-3">
                <ProviderPanelActions
                    name={name}
                    gradient={gradient}
                    isLoadingStatus={isLoadingStatus}
                    isConnected={Boolean(status?.isConnected)}
                    isImporting={isImporting}
                    isDisconnecting={isDisconnecting}
                    onConnect={handleConnect}
                    onImport={handleImport}
                    onDisconnect={handleDisconnect}
                />
            </div>
        </GlassCard>
    );
}
