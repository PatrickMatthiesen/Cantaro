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
        <section className="rounded-[1.5rem] border border-white/80 bg-white/70 p-6 text-slate-700 shadow-[0_12px_34px_rgba(88,74,150,0.06)] backdrop-blur">
            <p className="text-sm font-semibold">Loading sync controls…</p>
        </section>
    );
}

export function SyncEmptyState() {
    return (
        <section className="rounded-[1.5rem] border border-white/80 bg-white/70 p-6 text-slate-900 shadow-[0_12px_34px_rgba(88,74,150,0.06)] backdrop-blur">
            <p className="text-xs font-black tracking-[0.18em] text-violet-700 uppercase">Playlist sync</p>
            <h3 className="mt-2 text-xl font-black">Ready when you are</h3>
            <p className="mt-1 text-sm font-semibold text-slate-600">Connect a music platform to import playlists into Cantaro's archive.</p>
        </section>
    );
}

function SyncUsageSummary({ syncStatus, windowUsagePercent, songSyncLimit, songsSyncedInWindow, remainingSongs }: SyncUsageSummaryProps) {
    return (
        <div className="mt-4 rounded-2xl bg-white/75 p-4">
            <div className="mb-1 flex items-center justify-between text-xs font-semibold text-slate-600">
                <span>Current window usage</span>
                <span>
                    {songsSyncedInWindow.toLocaleString()} / {songSyncLimit.toLocaleString()} songs
                </span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-slate-200">
                <div className="h-full bg-violet-600" style={{ width: `${windowUsagePercent}%` }} />
            </div>
            <p className="mt-2 text-xs font-semibold text-slate-500">{remainingSongs.toLocaleString()} songs remaining in current window.</p>
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
                className="rounded-xl bg-slate-950 px-4 py-2 text-sm font-black text-white transition hover:bg-violet-700 disabled:cursor-not-allowed disabled:bg-slate-200 disabled:text-slate-500"
            >
                {isSyncing ? `Syncing… ${syncProgress}%` : 'Sync everything'}
            </button>
            <button
                onClick={onTogglePlaylistSelector}
                disabled={!canSync}
                className="rounded-xl border border-violet-200 bg-white px-4 py-2 text-sm font-black text-violet-700 transition hover:bg-violet-50 disabled:cursor-not-allowed disabled:border-slate-200 disabled:text-slate-400"
            >
                {showPlaylistSelector ? 'Hide playlists' : 'Choose playlists'}
            </button>
            <button
                onClick={onToggleStatusDrawer}
                className="rounded-xl bg-white px-4 py-2 text-sm font-black text-slate-700 transition hover:bg-slate-100"
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
            <div className="mb-1 flex items-center justify-between text-xs font-semibold text-slate-600">
                <span>Sync progress</span>
                <span>{syncProgress}%</span>
            </div>
            <div className="h-3 overflow-hidden rounded-full bg-slate-200">
                <div
                    className="h-full bg-violet-600 transition-all duration-300"
                    style={{ width: `${syncProgress}%` }}
                />
            </div>
        </div>
    );
}

function SyncStatusDrawer({ statusUpdates }: SyncStatusDrawerProps) {
    return (
        <div className="mt-4 rounded-2xl border border-white/80 bg-white/80 p-4">
            <p className="text-xs font-black tracking-[0.18em] text-slate-500 uppercase">Sync status updates</p>
            <ul className="mt-3 max-h-44 space-y-2 overflow-y-auto text-sm text-slate-700">
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
            <p className="text-sm font-black text-slate-800">Choose playlists</p>
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
                            className="h-4 w-4 rounded border-slate-300 text-violet-600 focus:ring-2 focus:ring-violet-500"
                        />
                        <div className="flex-1">
                            <p className="text-sm font-semibold text-slate-800">{playlist.title}</p>
                            <p className="text-xs text-slate-500">{playlist.itemCount} songs</p>
                        </div>
                    </label>
                ))}
            </div>
            <div className="mt-4 flex gap-2">
                <button
                    onClick={onSyncSelected}
                    disabled={selectedPlaylists.size === 0 || isSyncing}
                    className="flex-1 rounded-xl bg-slate-950 px-4 py-2 text-sm font-black text-white transition hover:bg-violet-700 disabled:cursor-not-allowed disabled:bg-slate-200 disabled:text-slate-500"
                >
                    Sync selected
                </button>
                <button
                    onClick={onClose}
                    className="rounded-xl bg-slate-100 px-4 py-2 text-sm font-black text-slate-700 transition hover:bg-slate-200"
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
        <section className="rounded-[1.5rem] border border-white/80 bg-white/70 p-6 text-slate-900 shadow-[0_12px_34px_rgba(88,74,150,0.06)] backdrop-blur">
            <p className="text-xs font-black tracking-[0.18em] text-violet-700 uppercase">Playlist sync</p>
            <h3 className="mt-2 text-xl font-black">Keep your playlists aligned</h3>
            <p className="mt-1 text-sm font-semibold text-slate-600">
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
