import { useState, useEffect, useCallback, useRef } from 'react';
import { GlassCard, GradientButton, StatusBadge } from '../components/ui/GlassComponents';
import { mediaApi } from '../services/mediaApi';
import { mediaProviderCatalog } from '../services/mediaProviders';
import type { MediaProviderAccountStatusDto, MediaImportDto } from '../services/mediaApi';

function providerLastRemoteCheckStorageKey(providerId: string): string {
  return `cantaro.media.provider.${providerId}.lastRemoteCheckAt`;
}

function persistRemoteCheckTimestamp(providerId: string, importedAt: string): void {
  try {
    window.localStorage.setItem(providerLastRemoteCheckStorageKey(providerId), importedAt);
  } catch {
    // Local storage is best-effort only.
  }
}

function clearRemoteCheckTimestamp(providerId: string): void {
  try {
    window.localStorage.removeItem(providerLastRemoteCheckStorageKey(providerId));
  } catch {
    // Local storage is best-effort only.
  }
}

function shouldAutoImportAfterConnect(providerId: string): boolean {
  const search = new URLSearchParams(window.location.search);
  return search.get('connected') === 'true' && search.get('provider') === providerId;
}

function clearConnectSearchParams(): void {
  const url = new URL(window.location.href);
  url.searchParams.delete('connected');
  url.searchParams.delete('provider');
  url.searchParams.delete('trigger');
  window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`);
}

interface MediaProvidersPageProps {
  onNavigateHome: () => void;
  onNavigateLibrary?: () => void;
}

interface ProviderPanelProps {
  providerId: string;
  name: string;
  icon: string;
  gradient: string;
  description: string;
}

function ProviderPanel({ providerId, name, icon, gradient, description }: ProviderPanelProps) {
  const [status, setStatus] = useState<MediaProviderAccountStatusDto | null>(null);
  const [isLoadingStatus, setIsLoadingStatus] = useState(true);
  const [isImporting, setIsImporting] = useState(false);
  const [isDisconnecting, setIsDisconnecting] = useState(false);
  const [lastImport, setLastImport] = useState<MediaImportDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const hasTriggeredConnectedImport = useRef(false);

  const loadStatus = useCallback(async () => {
    setIsLoadingStatus(true);
    setError(null);
    try {
      const data = await mediaApi.getProviderStatus(providerId);
      setStatus(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load status');
    } finally {
      setIsLoadingStatus(false);
    }
  }, [providerId]);

  useEffect(() => {
    void loadStatus();
  }, [loadStatus]);

  const handleConnect = () => {
    mediaApi.connectProvider(providerId, {
      route: window.location.pathname,
      trigger: 'media-providers-page',
    });
  };

  const handleDisconnect = async () => {
    setIsDisconnecting(true);
    setError(null);
    try {
      await mediaApi.disconnectProvider(providerId);
      setStatus((prev) => (prev ? { ...prev, isConnected: false, displayName: undefined } : null));
      setLastImport(null);
      clearRemoteCheckTimestamp(providerId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to disconnect');
    } finally {
      setIsDisconnecting(false);
    }
  };

  const handleImport = useCallback(async () => {
    setIsImporting(true);
    setError(null);
    try {
      const result = await mediaApi.importLibrary(providerId);
      setLastImport(result);
      persistRemoteCheckTimestamp(providerId, result.importedAt);
      return result;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Import failed');
      return null;
    } finally {
      setIsImporting(false);
    }
  }, [providerId]);

  useEffect(() => {
    if (!status?.isConnected || hasTriggeredConnectedImport.current || !shouldAutoImportAfterConnect(providerId)) {
      return;
    }

    hasTriggeredConnectedImport.current = true;
    void handleImport().finally(() => {
      clearConnectSearchParams();
    });
  }, [handleImport, providerId, status?.isConnected]);

  return (
    <GlassCard className="p-6">
      <div className="flex items-start gap-4">
        <div
          className={`flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-linear-to-br ${gradient} text-2xl text-white shadow-sm`}
          aria-hidden
        >
          {icon}
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-3">
            <h3 className="text-xl font-semibold text-gray-900">{name}</h3>
            {!isLoadingStatus && status ? (
              <StatusBadge status={status.isConnected ? 'connected' : 'available'} />
            ) : null}
          </div>

          <p className="mt-1 text-sm text-gray-600">{description}</p>

          {status?.isConnected && status.displayName ? (
            <p className="mt-1 text-xs text-gray-500">
              Signed in as <span className="font-medium text-gray-700">{status.displayName}</span>
            </p>
          ) : null}
        </div>
      </div>

      {error ? (
        <div className="mt-4 rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</div>
      ) : null}

      {lastImport ? (
        <div className="mt-4 rounded-2xl bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          <p className="font-semibold">Import complete</p>
          <ul className="mt-1 space-y-0.5 text-xs text-emerald-700">
            <li>{lastImport.importedCount} entries imported</li>
            <li>{lastImport.createdTitles} new titles created</li>
            <li>
              {lastImport.createdEntries} entries added · {lastImport.updatedEntries} entries updated
            </li>
          </ul>
        </div>
      ) : null}

      <div className="mt-5 flex flex-wrap gap-3">
        {isLoadingStatus ? (
          <span className="text-xs text-gray-500">Checking connection…</span>
        ) : status?.isConnected ? (
          <>
            <GradientButton
              gradient={gradient}
              onClick={() => void handleImport()}
              disabled={isImporting}
              aria-busy={isImporting}
            >
              {isImporting ? 'Refreshing…' : 'Refresh library'}
            </GradientButton>
            <GradientButton
              tone="soft"
              onClick={() => void handleDisconnect()}
              disabled={isDisconnecting}
              aria-busy={isDisconnecting}
            >
              {isDisconnecting ? 'Disconnecting…' : 'Disconnect'}
            </GradientButton>
          </>
        ) : (
          <GradientButton gradient={gradient} onClick={handleConnect}>
            Connect {name}
          </GradientButton>
        )}
      </div>
    </GlassCard>
  );
}

export function MediaProvidersPage({ onNavigateHome, onNavigateLibrary }: MediaProvidersPageProps) {
  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto max-w-3xl space-y-6 px-6 pt-8 pb-16">
        <header className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Cantaro · Media</p>
            <h1 className="mt-1 text-3xl font-bold">Media providers</h1>
          </div>
          <div className="flex gap-2">
            {onNavigateLibrary ? (
              <GradientButton gradient="from-blue-500 to-cyan-500" onClick={onNavigateLibrary}>
                View Library
              </GradientButton>
            ) : null}
            <GradientButton tone="soft" onClick={onNavigateHome}>
              ← Back to home
            </GradientButton>
          </div>
        </header>

        <GlassCard className="p-5">
          <p className="text-sm text-gray-700">
            Connect media tracking services to import your anime and manga library into Cantaro. Once
            connected, you can import your list and track progress across providers.
          </p>
        </GlassCard>

        <section className="space-y-4" aria-label="Media provider accounts">
          {mediaProviderCatalog.map((provider) => (
            <ProviderPanel
              key={provider.id}
              providerId={provider.id}
              name={provider.name}
              icon={provider.icon}
              gradient={provider.gradient}
              description={provider.description}
            />
          ))}
        </section>
      </div>
    </div>
  );
}
