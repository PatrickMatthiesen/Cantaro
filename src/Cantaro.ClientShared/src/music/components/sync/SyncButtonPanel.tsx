import type { BatchSyncResponse, SyncStatusResponse } from '../../services/syncApi';
import type { PlatformPlaylist } from '../../platforms';

interface SyncUsageSummaryProps {
    syncStatus: SyncStatusResponse;
    windowUsagePercent: number;
    songSyncLimit: number;
    songsSyncedInWindow: number;
    remainingSongs: number;
}

interface SyncActionBarProps {
    canSync: boolean;
    isSyncing: boolean;
    syncProgress: number;
    showPlaylistSelector: boolean;
    showStatusDrawer: boolean;
    onSyncAll: () => void;
    onTogglePlaylistSelector: () => void;
    onToggleStatusDrawer: () => void;
}

interface SyncProgressPanelProps {
    isSyncing: boolean;
    syncProgress: number;
}

interface SyncStatusDrawerProps {
    statusUpdates: string[];
}

interface SyncResultPanelProps {
    syncResult: BatchSyncResponse;
}

interface PlaylistSelectorProps {
    availablePlaylists: PlatformPlaylist[];
    selectedPlaylists: Set<string>;
    isSyncing: boolean;
    onTogglePlaylistSelection: (playlistId: string) => void;
    onSyncSelected: () => void;
    onClose: () => void;
}

export interface SyncButtonPanelProps {
    platformName: string;
    syncStatus: SyncStatusResponse;
    availablePlaylists: PlatformPlaylist[];
    isSyncing: boolean;
    syncProgress: number;
    statusUpdates: string[];
    showStatusDrawer: boolean;
    showPlaylistSelector: boolean;
    selectedPlaylists: Set<string>;
    syncResult: BatchSyncResponse | null;
    error: string | null;
    songsSyncedInWindow: number;
    songSyncLimit: number;
    windowMinutes: number;
    remainingSongs: number;
    windowUsagePercent: number;
    canSync: boolean;
    onSyncAll: () => void;
    onTogglePlaylistSelector: () => void;
    onToggleStatusDrawer: () => void;
    onTogglePlaylistSelection: (playlistId: string) => void;
    onSyncSelected: () => void;
    onClosePlaylistSelector: () => void;
}

export function SyncLoadingState() {
    return (
        <section className="rounded-[1.5rem] border border-border-subtle bg-surface-translucent p-6 text-content shadow-[0_12px_34px_rgba(88,74,150,0.06)] backdrop-blur">
            <p className="text-sm font-semibold">Loading sync controls…</p>
        </section>
    );
}

export function SyncEmptyState() {
    return (
        <section className="rounded-[1.5rem] border border-border-subtle bg-surface-translucent p-6 text-content shadow-[0_12px_34px_rgba(88,74,150,0.06)] backdrop-blur">
            <p className="text-xs font-black tracking-[0.18em] text-accent-strong uppercase">Playlist sync</p>
            <h3 className="mt-2 text-xl font-black">Ready when you are</h3>
            <p className="mt-1 text-sm font-semibold text-content-muted">Connect a music platform to import playlists into Cantaro's archive.</p>
        </section>
    );
}

function SyncUsageSummary({ syncStatus, windowUsagePercent, songSyncLimit, songsSyncedInWindow, remainingSongs }: SyncUsageSummaryProps) {
    return (
        <div className="mt-4 rounded-2xl bg-surface-translucent p-4">
            <div className="mb-1 flex items-center justify-between text-xs font-semibold text-content-muted">
                <span>Current window usage</span>
                <span>
                    {songsSyncedInWindow.toLocaleString()} / {songSyncLimit.toLocaleString()} songs
                </span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-surface-subtle">
                <div className="h-full bg-accent" style={{ width: `${windowUsagePercent}%` }} />
            </div>
            <p className="mt-2 text-xs font-semibold text-content-muted">{remainingSongs.toLocaleString()} songs remaining in current window.</p>
            {syncStatus.overall.message ? <p className="mt-2 text-xs font-medium text-warning-content">{syncStatus.overall.message}</p> : null}
        </div>
    );
}

function SyncActionBar({
    canSync,
    isSyncing,
    syncProgress,
    showPlaylistSelector,
    showStatusDrawer,
    onSyncAll,
    onTogglePlaylistSelector,
    onToggleStatusDrawer,
}: SyncActionBarProps) {
    return (
        <div className="mt-4 flex flex-wrap gap-2">
            <button
                onClick={onSyncAll}
                disabled={!canSync}
                className="rounded-xl bg-action px-4 py-2 text-sm font-black text-action-content transition hover:bg-action-hover disabled:cursor-not-allowed disabled:bg-surface-subtle disabled:text-content-subtle"
            >
                {isSyncing ? `Syncing… ${syncProgress}%` : 'Sync everything'}
            </button>
            <button
                onClick={onTogglePlaylistSelector}
                disabled={!canSync}
                className="rounded-xl border border-border-subtle bg-surface px-4 py-2 text-sm font-black text-accent-strong transition hover:bg-accent-soft disabled:cursor-not-allowed disabled:border-border-subtle disabled:text-content-subtle"
            >
                {showPlaylistSelector ? 'Hide playlists' : 'Choose playlists'}
            </button>
            <button
                onClick={onToggleStatusDrawer}
                className="rounded-xl bg-surface px-4 py-2 text-sm font-black text-content transition hover:bg-surface-hover"
            >
                {showStatusDrawer ? 'Hide status' : 'Show status'}
            </button>
        </div>
    );
}

function SyncProgressPanel({ isSyncing, syncProgress }: SyncProgressPanelProps) {
    if (!isSyncing && syncProgress <= 0) {
        return null;
    }

    return (
        <div className="mt-4">
            <div className="mb-1 flex items-center justify-between text-xs font-semibold text-content-muted">
                <span>Sync progress</span>
                <span>{syncProgress}%</span>
            </div>
            <div className="h-3 overflow-hidden rounded-full bg-surface-subtle">
                <div
                    className="h-full bg-accent transition-all duration-300"
                    style={{ width: `${syncProgress}%` }}
                />
            </div>
        </div>
    );
}

function SyncStatusDrawer({ statusUpdates }: SyncStatusDrawerProps) {
    return (
        <div className="mt-4 rounded-2xl border border-border-subtle bg-surface-translucent p-4">
            <p className="text-xs font-black tracking-[0.18em] text-content-muted uppercase">Sync status updates</p>
            <ul className="mt-3 max-h-44 space-y-2 overflow-y-auto text-sm text-content">
                {statusUpdates.map((update, index) => (
                    <li key={`${update}-${index}`} className="rounded-xl bg-surface px-3 py-2">
                        {update}
                    </li>
                ))}
            </ul>
        </div>
    );
}

function SyncResultPanel({ syncResult }: SyncResultPanelProps) {
    const failedResults = syncResult.results.filter((result) => !result.success);

    return (
        <div className="mt-4 rounded-xl border border-success-border bg-success-surface px-4 py-3 text-sm text-success-content">
            Processed {syncResult.songsSynced}/{syncResult.songsRequested} requested songs.
            {failedResults.length > 0 ? (
                <ul className="mt-2 list-disc space-y-1 pl-4">
                    {failedResults.map((result) => (
                        <li key={result.servicePlaylistId}>
                            {result.playlistName}: {result.error}
                        </li>
                    ))}
                </ul>
            ) : null}
        </div>
    );
}

function PlaylistSelector({
    availablePlaylists,
    selectedPlaylists,
    isSyncing,
    onTogglePlaylistSelection,
    onSyncSelected,
    onClose,
}: PlaylistSelectorProps) {
    return (
        <div className="mt-4 rounded-2xl border border-border-subtle bg-surface-translucent p-4">
            <p className="text-sm font-black text-content">Choose playlists</p>
            <div className="mt-3 max-h-64 space-y-2 overflow-y-auto">
                {availablePlaylists.map((playlist) => (
                    <label
                        key={playlist.id}
                        className="flex cursor-pointer items-center gap-3 rounded-lg border border-border-subtle bg-surface p-3 transition hover:border-border-strong"
                    >
                        <input
                            type="checkbox"
                            checked={selectedPlaylists.has(playlist.id)}
                            onChange={() => onTogglePlaylistSelection(playlist.id)}
                            className="h-4 w-4 rounded border-border-strong text-accent focus:ring-2 focus:ring-focus"
                        />
                        <div className="flex-1">
                            <p className="text-sm font-semibold text-content">{playlist.title}</p>
                            <p className="text-xs text-content-muted">{playlist.itemCount} songs</p>
                        </div>
                    </label>
                ))}
            </div>
            <div className="mt-4 flex gap-2">
                <button
                    onClick={onSyncSelected}
                    disabled={selectedPlaylists.size === 0 || isSyncing}
                    className="flex-1 rounded-xl bg-action px-4 py-2 text-sm font-black text-action-content transition hover:bg-action-hover disabled:cursor-not-allowed disabled:bg-surface-subtle disabled:text-content-subtle"
                >
                    Sync selected
                </button>
                <button
                    onClick={onClose}
                    className="rounded-xl bg-surface-subtle px-4 py-2 text-sm font-black text-content transition hover:bg-surface-hover"
                >
                    Cancel
                </button>
            </div>
        </div>
    );
}

function SyncButtonFeedback({
    error,
    syncResult,
    showPlaylistSelector,
    availablePlaylists,
    selectedPlaylists,
    isSyncing,
    onTogglePlaylistSelection,
    onSyncSelected,
    onClosePlaylistSelector,
}: Pick<
    SyncButtonPanelProps,
    'error'
    | 'syncResult'
    | 'showPlaylistSelector'
    | 'availablePlaylists'
    | 'selectedPlaylists'
    | 'isSyncing'
    | 'onTogglePlaylistSelection'
    | 'onSyncSelected'
    | 'onClosePlaylistSelector'
>) {
    return (
        <>
            {error ? (
                <div className="mt-4 rounded-xl border border-danger-border bg-danger-surface px-4 py-3 text-sm text-danger-content">
                    {error}
                </div>
            ) : null}

            {syncResult ? <SyncResultPanel syncResult={syncResult} /> : null}

            {showPlaylistSelector && availablePlaylists.length > 0 ? (
                <PlaylistSelector
                    availablePlaylists={availablePlaylists}
                    selectedPlaylists={selectedPlaylists}
                    isSyncing={isSyncing}
                    onTogglePlaylistSelection={onTogglePlaylistSelection}
                    onSyncSelected={onSyncSelected}
                    onClose={onClosePlaylistSelector}
                />
            ) : null}
        </>
    );
}

function SyncButtonHeader({
    platformName,
    songsSyncedInWindow,
    songSyncLimit,
    windowMinutes,
    remainingSongs,
    windowUsagePercent,
    syncStatus,
}: Pick<
    SyncButtonPanelProps,
    'platformName'
    | 'songsSyncedInWindow'
    | 'songSyncLimit'
    | 'windowMinutes'
    | 'remainingSongs'
    | 'windowUsagePercent'
    | 'syncStatus'
>) {
    return (
        <>
            <p className="text-xs font-black tracking-[0.18em] text-accent-strong uppercase">Playlist sync</p>
            <h3 className="mt-2 text-xl font-black">Keep your playlists aligned</h3>
            <p className="mt-1 text-sm font-semibold text-content-muted">
                {platformName} limit: {songSyncLimit.toLocaleString()} songs per {windowMinutes} minutes.
            </p>
            <SyncUsageSummary
                syncStatus={syncStatus}
                windowUsagePercent={windowUsagePercent}
                songSyncLimit={songSyncLimit}
                songsSyncedInWindow={songsSyncedInWindow}
                remainingSongs={remainingSongs}
            />
        </>
    );
}

function SyncButtonControls({
    canSync,
    isSyncing,
    syncProgress,
    showPlaylistSelector,
    showStatusDrawer,
    onSyncAll,
    onTogglePlaylistSelector,
    onToggleStatusDrawer,
}: Pick<
    SyncButtonPanelProps,
    'canSync'
    | 'isSyncing'
    | 'syncProgress'
    | 'showPlaylistSelector'
    | 'showStatusDrawer'
    | 'onSyncAll'
    | 'onTogglePlaylistSelector'
    | 'onToggleStatusDrawer'
>) {
    return (
        <>
            <SyncActionBar
                canSync={canSync}
                isSyncing={isSyncing}
                syncProgress={syncProgress}
                showPlaylistSelector={showPlaylistSelector}
                showStatusDrawer={showStatusDrawer}
                onSyncAll={onSyncAll}
                onTogglePlaylistSelector={onTogglePlaylistSelector}
                onToggleStatusDrawer={onToggleStatusDrawer}
            />
            <SyncProgressPanel isSyncing={isSyncing} syncProgress={syncProgress} />
        </>
    );
}

function SyncButtonPanelContent(props: SyncButtonPanelProps) {
    return (
        <>
            <SyncButtonHeader {...props} />
            <SyncButtonControls {...props} />
            {props.showStatusDrawer && props.statusUpdates.length > 0 ? <SyncStatusDrawer statusUpdates={props.statusUpdates} /> : null}

            <SyncButtonFeedback
                error={props.error}
                syncResult={props.syncResult}
                showPlaylistSelector={props.showPlaylistSelector}
                availablePlaylists={props.availablePlaylists}
                selectedPlaylists={props.selectedPlaylists}
                isSyncing={props.isSyncing}
                onTogglePlaylistSelection={props.onTogglePlaylistSelection}
                onSyncSelected={props.onSyncSelected}
                onClosePlaylistSelector={props.onClosePlaylistSelector}
            />
        </>
    );
}

export function SyncButtonPanel(props: SyncButtonPanelProps) {
    return (
        <section className="rounded-[1.5rem] border border-border-subtle bg-surface-translucent p-6 text-content shadow-[0_12px_34px_rgba(88,74,150,0.06)] backdrop-blur">
            <SyncButtonPanelContent {...props} />
        </section>
    );
}
