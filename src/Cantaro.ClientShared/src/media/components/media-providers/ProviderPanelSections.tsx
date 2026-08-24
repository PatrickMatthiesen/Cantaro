import { ActionButton } from '../../../ui';
import { ArrowRightLeft, Check, RefreshCw, Unplug } from 'lucide-react';
import type { ReactNode } from 'react';
import type {
    MediaImportDto,
    MediaImportRequestDto,
    MediaInitialSyncApplyResultDto,
    MediaInitialSyncPreviewDto,
    MediaProviderAccountStatusDto,
} from '../../services/mediaApi';

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
    onConnect: () => void;
    onRetryStatus: () => Promise<void>;
    connectedActions: ConnectedProviderActionsProps;
}

interface ConnectedProviderActionsProps {
    isImporting: boolean;
    isDisconnecting: boolean;
    isInitialSyncActive: boolean;
    onImport: () => Promise<MediaImportRequestDto | null>;
    onPreviewInitialSync: () => Promise<MediaInitialSyncPreviewDto | null>;
    onDisconnect: () => Promise<void>;
}

interface ProviderPanelInitialSyncProps {
    name: string;
    preview: MediaInitialSyncPreviewDto | null;
    result: MediaInitialSyncApplyResultDto | null;
    isPreviewing: boolean;
    isApplying: boolean;
    onApply: () => Promise<MediaInitialSyncApplyResultDto | null>;
    onRetry: () => Promise<MediaInitialSyncPreviewDto | null>;
    onDismiss: () => void;
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

function InitialSyncMetrics({ preview }: { preview: MediaInitialSyncPreviewDto }) {
    return (
        <dl className="mt-4 grid grid-cols-2 gap-x-6 gap-y-3 text-sm sm:grid-cols-4">
            <div><dt className="text-content-muted">Will add</dt><dd className="mt-0.5 font-bold text-content">{preview.willAdd}</dd></div>
            <div><dt className="text-content-muted">Will update</dt><dd className="mt-0.5 font-bold text-content">{preview.willUpdate}</dd></div>
            <div><dt className="text-content-muted">Already aligned</dt><dd className="mt-0.5 font-bold text-content">{preview.alreadyAligned}</dd></div>
            <div><dt className="text-content-muted">Needs matching</dt><dd className="mt-0.5 font-bold text-content">{preview.needsMatching}</dd></div>
        </dl>
    );
}

function InitialSyncUnresolvedTitles({ preview }: { preview: MediaInitialSyncPreviewDto }) {
    if (preview.unresolvedTitles.length === 0) {
        return null;
    }

    const visibleTitles = preview.unresolvedTitles.slice(0, 5);
    const remainingCount = preview.unresolvedTitles.length - visibleTitles.length;
    return (
        <details className="mt-4 text-sm text-content-muted">
            <summary className="w-fit cursor-pointer font-semibold text-content hover:text-personal-accent-strong">
                Titles that need matching
            </summary>
            <ul className="mt-2 grid gap-1.5 pl-5">
                {visibleTitles.map((title) => (
                    <li key={title.mediaTitleId} className="list-disc">
                        <span className="text-content">{title.title}</span>
                        <span> · {title.mediaKind}</span>
                    </li>
                ))}
            </ul>
            {remainingCount > 0 ? <p className="mt-2">And {remainingCount} more.</p> : null}
        </details>
    );
}

function InitialSyncResult({
    name,
    result,
    onDismiss,
}: Pick<ProviderPanelInitialSyncProps, 'name' | 'result' | 'onDismiss'> & {
    result: MediaInitialSyncApplyResultDto;
}) {
    const changedCount = result.added + result.updated;
    return (
        <section className="mt-6 border-y border-success-border py-4 sm:ml-16" aria-live="polite">
            <p className="flex items-center gap-2 text-sm font-semibold text-success-content">
                <Check className="h-4 w-4" aria-hidden />
                {result.status === 'queued' ? 'Sync queued' : 'Sync complete'}
            </p>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-content-muted">
                {changedCount > 0
                    ? `${changedCount} ${changedCount === 1 ? 'title' : 'titles'} ${result.status === 'queued' ? 'will be aligned' : 'were aligned'} with ${name}.`
                    : `${name} was already aligned with Cantaro.`}
            </p>
            <dl className="mt-4 grid grid-cols-2 gap-x-6 gap-y-3 text-sm sm:grid-cols-4">
                <div><dt className="text-content-muted">Added</dt><dd className="mt-0.5 font-bold text-content">{result.added}</dd></div>
                <div><dt className="text-content-muted">Updated</dt><dd className="mt-0.5 font-bold text-content">{result.updated}</dd></div>
                <div><dt className="text-content-muted">Already aligned</dt><dd className="mt-0.5 font-bold text-content">{result.alreadyAligned}</dd></div>
                <div><dt className="text-content-muted">Needs matching</dt><dd className="mt-0.5 font-bold text-content">{result.needsMatching}</dd></div>
            </dl>
            <p className="mt-3 text-sm text-content-muted">
                {result.providerOnly} {result.providerOnly === 1 ? 'title' : 'titles'} only in {name} were left unchanged.
            </p>
            <ActionButton tone="ghost" className="mt-3" onClick={onDismiss}>Done</ActionButton>
        </section>
    );
}

function InitialSyncBusy({
    name,
    preview,
    onDismiss,
}: Pick<ProviderPanelInitialSyncProps, 'name' | 'preview' | 'onDismiss'>) {
    const message = getInitialSyncBusyMessage(name, preview);
    return (
        <section className="mt-6 border-y border-info-border py-4 sm:ml-16" aria-live="polite" aria-busy="true">
            <p className="flex items-center gap-2 text-sm font-semibold text-content">
                <RefreshCw className="h-4 w-4 animate-spin motion-reduce:animate-none" aria-hidden />
                Preparing a safe sync…
            </p>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-content-muted">
                {message}
            </p>
            <ActionButton tone="ghost" className="mt-4" onClick={onDismiss}>Not now</ActionButton>
        </section>
    );
}

function getInitialSyncBusyMessage(name: string, preview: MediaInitialSyncPreviewDto | null): string {
    if (!preview) {
        return `Refreshing your connected providers before Cantaro compares its library with ${name}.`;
    }
    if (preview.message) {
        return preview.message;
    }
    if (preview.pendingOperations === 0) {
        return `Refreshing your connected providers before Cantaro compares its library with ${name}.`;
    }
    const noun = preview.pendingOperations === 1 ? 'change' : 'changes';
    return `Waiting for ${preview.pendingOperations} existing provider ${noun} to settle before comparing libraries.`;
}

function InitialSyncBlocked({
    name,
    preview,
    isPreviewing,
    onRetry,
    onDismiss,
}: Pick<ProviderPanelInitialSyncProps, 'name' | 'preview' | 'isPreviewing' | 'onRetry' | 'onDismiss'> & {
    preview: MediaInitialSyncPreviewDto;
}) {
    return (
        <section className="mt-6 border-y border-warning-border py-4 sm:ml-16" aria-live="polite">
            <p className="flex items-center gap-2 text-sm font-semibold text-content">
                <ArrowRightLeft className="h-4 w-4 text-personal-accent-strong" aria-hidden /> Sync Cantaro to {name}
            </p>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-warning-content">
                {preview.message ?? `Cantaro couldn’t finish preparing the ${name} comparison yet.`}
            </p>
            <div className="mt-4 flex flex-wrap gap-3">
                <ActionButton tone="secondary" onClick={() => void onRetry()} disabled={isPreviewing}>
                    <RefreshCw className="h-4 w-4" aria-hidden /> Try again
                </ActionButton>
                <ActionButton tone="ghost" onClick={onDismiss}>Not now</ActionButton>
            </div>
        </section>
    );
}

function InitialSyncReady({
    name,
    preview,
    isApplying,
    onApply,
    onDismiss,
}: Pick<ProviderPanelInitialSyncProps, 'name' | 'preview' | 'isApplying' | 'onApply' | 'onDismiss'> & {
    preview: MediaInitialSyncPreviewDto;
}) {
    return (
        <section className="mt-6 border-y border-info-border py-4 sm:ml-16" aria-live="polite">
            <p className="flex items-center gap-2 text-sm font-semibold text-content">
                <ArrowRightLeft className="h-4 w-4 text-personal-accent-strong" aria-hidden /> Sync Cantaro to {name}
            </p>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-content-muted">
                Review what Cantaro will copy. {preview.providerOnly} {preview.providerOnly === 1 ? 'title' : 'titles'} only in {name} will be left unchanged.
            </p>
            <InitialSyncMetrics preview={preview} />
            <InitialSyncUnresolvedTitles preview={preview} />
            <div className="mt-4 flex flex-wrap gap-3">
                <ActionButton
                    tone="personal"
                    onClick={() => void onApply()}
                    disabled={isApplying}
                    aria-busy={isApplying}
                    busyLabel={`Syncing to ${name}…`}
                >
                    <ArrowRightLeft className="h-4 w-4" aria-hidden /> Sync Cantaro to {name}
                </ActionButton>
                <ActionButton tone="ghost" onClick={onDismiss} disabled={isApplying}>Not now</ActionButton>
            </div>
        </section>
    );
}

export function ProviderPanelInitialSync(props: ProviderPanelInitialSyncProps) {
    if (props.result) {
        return <InitialSyncResult name={props.name} result={props.result} onDismiss={props.onDismiss} />;
    }
    if (!props.preview) {
        return <InitialSyncBusy name={props.name} preview={null} onDismiss={props.onDismiss} />;
    }
    if (props.isPreviewing || props.preview.status === 'settling') {
        return <InitialSyncBusy name={props.name} preview={props.preview} onDismiss={props.onDismiss} />;
    }
    if (props.preview.status === 'blocked') {
        return <InitialSyncBlocked {...props} preview={props.preview} />;
    }
    return <InitialSyncReady {...props} preview={props.preview} />;
}

function ConnectedProviderActions({
    isImporting,
    isDisconnecting,
    isInitialSyncActive,
    onImport,
    onPreviewInitialSync,
    onDisconnect,
}: ConnectedProviderActionsProps) {
    const providerActionInProgress = [isImporting, isDisconnecting].some(Boolean);
    const allActionsDisabled = [providerActionInProgress, isInitialSyncActive].some(Boolean);
    return (
        <>
            {!isInitialSyncActive ? (
                <ActionButton
                    tone="personal"
                    onClick={() => void onPreviewInitialSync()}
                    disabled={providerActionInProgress}
                >
                    <ArrowRightLeft className="h-4 w-4" aria-hidden /> Sync Cantaro
                </ActionButton>
            ) : null}
            <ActionButton
                tone="secondary"
                onClick={() => void onImport()}
                disabled={allActionsDisabled}
                aria-busy={isImporting}
                busyLabel="Refreshing library…"
            >
                <RefreshCw className="h-4 w-4" aria-hidden /> Refresh library
            </ActionButton>
            <ActionButton
                tone="ghost"
                className="hover:bg-danger-surface hover:text-danger-content"
                onClick={() => void onDisconnect()}
                disabled={allActionsDisabled}
                aria-busy={isDisconnecting}
                busyLabel="Disconnecting…"
            >
                <Unplug className="h-4 w-4" aria-hidden /> Disconnect
            </ActionButton>
        </>
    );
}

export function ProviderPanelActions(props: ProviderPanelActionsProps) {
    if (props.isLoadingStatus) {
        return null;
    }

    if (!props.hasStatus) {
        return (
            <ActionButton tone="secondary" onClick={() => void props.onRetryStatus()}>
                <RefreshCw className="h-4 w-4" aria-hidden /> Retry
            </ActionButton>
        );
    }

    if (!props.isConnected) {
        return (
            <ActionButton tone="personal" onClick={props.onConnect}>
                Connect {props.name}
            </ActionButton>
        );
    }

    return <ConnectedProviderActions {...props.connectedActions} />;
}
