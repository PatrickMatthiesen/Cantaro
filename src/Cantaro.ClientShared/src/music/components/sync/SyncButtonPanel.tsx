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
        <section className="rounded-3xl border border-white/80 bg-white/70 p-6 text-gray-700 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px]">
            <p className="text-sm">Loading sync controls…</p>
        </section>
    );
}

export function SyncEmptyState() {
    return (
        <section className="rounded-3xl border border-white/80 bg-white/70 p-6 text-gray-900 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px]">
            <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Playlist sync</p>
            <h3 className="mt-2 text-xl font-semibold">Ready when you are</h3>
            <p className="mt-1 text-sm text-gray-600">Connect a service workspace to start your first sync run.</p>
        </section>
    );
}

function SyncUsageSummary({ syncStatus, windowUsagePercent, songSyncLimit, songsSyncedInWindow, remainingSongs }: SyncUsageSummaryProps) {
    return (
        <div className="mt-4 rounded-2xl bg-white/75 p-4">
            <div className="mb-1 flex items-center justify-between text-xs text-gray-600">
                <span>Current window usage</span>
                <span>
                    {songsSyncedInWindow.toLocaleString()} / {songSyncLimit.toLocaleString()} songs
                </span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-gray-200">
                <div className="h-full bg-linear-to-r from-indigo-500 to-purple-500" style={{ width: `${windowUsagePercent}%` }} />
            </div>
            <p className="mt-2 text-xs text-gray-500">{remainingSongs.toLocaleString()} songs remaining in current window.</p>
            {syncStatus.overall.message ? <p className="mt-2 text-xs font-medium text-amber-700">{syncStatus.overall.message}</p> : null}
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
                className="rounded-xl bg-linear-to-r from-blue-500 to-cyan-500 px-4 py-2 text-sm font-semibold text-white transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-40"
            >
                {isSyncing ? `Syncing… ${syncProgress}%` : 'Sync everything'}
            </button>
            <button
                onClick={onTogglePlaylistSelector}
                disabled={!canSync}
                className="rounded-xl bg-gray-900 px-4 py-2 text-sm font-semibold text-white transition hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-40"
            >
                {showPlaylistSelector ? 'Hide playlists' : 'Choose playlists'}
            </button>
            <button
                onClick={onToggleStatusDrawer}
                className="rounded-xl bg-white px-4 py-2 text-sm font-semibold text-gray-700 transition hover:bg-gray-100"
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
            <div className="mb-1 flex items-center justify-between text-xs text-gray-600">
                <span>Sync progress</span>
                <span>{syncProgress}%</span>
            </div>
            <div className="h-3 overflow-hidden rounded-full bg-gray-200">
                <div
                    className="h-full bg-linear-to-r from-blue-500 via-indigo-500 to-purple-500 transition-all duration-300"
                    style={{ width: `${syncProgress}%` }}
                />
            </div>
        </div>
    );
}

function SyncStatusDrawer({ statusUpdates }: SyncStatusDrawerProps) {
    return (
        <div className="mt-4 rounded-2xl border border-white/80 bg-white/80 p-4">
            <p className="text-xs tracking-[0.22em] text-gray-500 uppercase">Sync status updates</p>
            <ul className="mt-3 max-h-44 space-y-2 overflow-y-auto text-sm text-gray-700">
                {statusUpdates.map((update, index) => (
                    <li key={`${update}-${index}`} className="rounded-xl bg-white px-3 py-2">
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
        <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
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
        <div className="mt-4 rounded-2xl border border-white/80 bg-white/80 p-4">
            <p className="text-sm font-semibold text-gray-800">Choose playlists</p>
            <div className="mt-3 max-h-64 space-y-2 overflow-y-auto">
                {availablePlaylists.map((playlist) => (
                    <label
                        key={playlist.id}
                        className="flex cursor-pointer items-center gap-3 rounded-lg border border-gray-200 bg-white p-3 transition hover:border-gray-300"
                    >
                        <input
                            type="checkbox"
                            checked={selectedPlaylists.has(playlist.id)}
                            onChange={() => onTogglePlaylistSelection(playlist.id)}
                            className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-2 focus:ring-indigo-500"
                        />
                        <div className="flex-1">
                            <p className="text-sm font-medium text-gray-800">{playlist.title}</p>
                            <p className="text-xs text-gray-500">{playlist.itemCount} songs</p>
                        </div>
                    </label>
                ))}
            </div>
            <div className="mt-4 flex gap-2">
                <button
                    onClick={onSyncSelected}
                    disabled={selectedPlaylists.size === 0 || isSyncing}
                    className="flex-1 rounded-xl bg-linear-to-r from-indigo-500 to-purple-500 px-4 py-2 text-sm font-semibold text-white transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-40"
                >
                    Sync selected
                </button>
                <button
                    onClick={onClose}
                    className="rounded-xl bg-gray-100 px-4 py-2 text-sm font-semibold text-gray-700 transition hover:bg-gray-200"
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
                <div className="mt-4 rounded-xl border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-700">
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

export function SyncButtonPanel({
    platformName,
    syncStatus,
    availablePlaylists,
    isSyncing,
    syncProgress,
    statusUpdates,
    showStatusDrawer,
    showPlaylistSelector,
    selectedPlaylists,
    syncResult,
    error,
    songsSyncedInWindow,
    songSyncLimit,
    windowMinutes,
    remainingSongs,
    windowUsagePercent,
    canSync,
    onSyncAll,
    onTogglePlaylistSelector,
    onToggleStatusDrawer,
    onTogglePlaylistSelection,
    onSyncSelected,
    onClosePlaylistSelector,
}: SyncButtonPanelProps) {
    return (
        <section className="rounded-3xl border border-white/80 bg-white/70 p-6 text-gray-900 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px]">
            <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Playlist sync</p>
            <h3 className="mt-2 text-xl font-semibold">Keep your playlists aligned</h3>
            <p className="mt-1 text-sm text-gray-600">
                {platformName} limit: {songSyncLimit.toLocaleString()} songs per {windowMinutes} minutes.
            </p>

            <SyncUsageSummary
                syncStatus={syncStatus}
                windowUsagePercent={windowUsagePercent}
                songSyncLimit={songSyncLimit}
                songsSyncedInWindow={songsSyncedInWindow}
                remainingSongs={remainingSongs}
            />

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
            {showStatusDrawer && statusUpdates.length > 0 ? <SyncStatusDrawer statusUpdates={statusUpdates} /> : null}

            <SyncButtonFeedback
                error={error}
                syncResult={syncResult}
                showPlaylistSelector={showPlaylistSelector}
                availablePlaylists={availablePlaylists}
                selectedPlaylists={selectedPlaylists}
                isSyncing={isSyncing}
                onTogglePlaylistSelection={onTogglePlaylistSelection}
                onSyncSelected={onSyncSelected}
                onClosePlaylistSelector={onClosePlaylistSelector}
            />
        </section>
    );
}
