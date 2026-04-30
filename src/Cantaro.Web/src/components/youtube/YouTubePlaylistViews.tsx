import type { SVGProps } from 'react';
import { GlassCard, GradientButton } from '../ui/GlassComponents';
import type { PlatformAccountStatus, PlatformPlaylist, PlatformSong } from '../../platforms';

function YouTubeIcon({ ariaLabel, ...props }: { ariaLabel?: string } & SVGProps<SVGSVGElement>) {
    return (
        <svg
            width="24"
            height="24"
            viewBox="0 0 24 24"
            fill="currentColor"
            aria-hidden={!ariaLabel}
            role={ariaLabel ? 'img' : undefined}
            aria-label={ariaLabel}
            {...props}
        >
            <path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z" />
        </svg>
    );
}

interface YouTubeDisconnectedStateProps {
    onConnect: () => void;
}

interface YouTubePlaylistDetailViewProps {
    playlist: PlatformPlaylist;
    playlistItems: PlatformSong[];
    isLoadingItems: boolean;
    onBack: () => void;
}

interface YouTubePlaylistBrowserProps {
    status: PlatformAccountStatus;
    playlists: PlatformPlaylist[];
    onRefresh: () => void;
    onSelectPlaylist: (playlist: PlatformPlaylist) => void;
}

function YouTubePlaylistThumbnail({ thumbnailUrl, title }: { thumbnailUrl?: string; title: string }) {
    if (thumbnailUrl) {
        return <img src={thumbnailUrl} alt={title} className="h-44 w-full object-cover" />;
    }

    return (
        <div className="flex h-44 w-full items-center justify-center bg-white text-gray-500" aria-hidden>
            <YouTubeIcon />
        </div>
    );
}

function YouTubePlaylistCard({ playlist, onSelect }: { playlist: PlatformPlaylist; onSelect: (playlist: PlatformPlaylist) => void }) {
    return (
        <button
            onClick={() => onSelect(playlist)}
            className="group overflow-hidden rounded-3xl border border-white/80 bg-white/70 text-left shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px] transition-transform hover:scale-[1.01]"
        >
            <YouTubePlaylistThumbnail thumbnailUrl={playlist.thumbnailUrl} title={playlist.title} />
            <div className="p-4">
                <h3 className="line-clamp-1 text-lg font-semibold text-gray-900">{playlist.title}</h3>
                {playlist.description ? <p className="mt-2 line-clamp-2 text-sm text-gray-600">{playlist.description}</p> : null}
            </div>
        </button>
    );
}

function YouTubeSongList({ playlistItems }: { playlistItems: PlatformSong[] }) {
    if (playlistItems.length === 0) {
        return <p className="p-6 text-sm text-gray-600">This playlist has no items yet.</p>;
    }

    return (
        <ol className="space-y-2">
            {playlistItems.map((item, index) => (
                <li key={item.id} className="flex items-center gap-3 rounded-xl bg-white/70 p-3">
                    <span className="w-6 text-xs font-semibold text-gray-500">{index + 1}</span>
                    {item.thumbnailUrl ? (
                        <img src={item.thumbnailUrl} alt="" className="h-14 w-24 rounded-lg object-cover" />
                    ) : (
                        <div className="h-14 w-24 rounded-lg bg-white" aria-hidden />
                    )}
                    <div className="min-w-0 flex-1">
                        <p className="line-clamp-2 text-sm font-medium text-gray-800">{item.title}</p>
                        {item.artistName ? <p className="text-xs text-gray-500">{item.artistName}</p> : null}
                    </div>
                </li>
            ))}
        </ol>
    );
}

export function YouTubeDisconnectedState({ onConnect }: YouTubeDisconnectedStateProps) {
    return (
        <GlassCard className="p-8">
            <div className="inline-flex items-center gap-2 rounded-full bg-red-50 px-3 py-1 text-xs font-semibold text-red-700 uppercase">
                <YouTubeIcon /> Not connected
            </div>
            <h2 className="mt-4 text-2xl font-semibold">Connect YouTube to begin</h2>
            <p className="mt-2 text-sm text-gray-600">
                Once connected, you can browse playlists and sync them through Cantaro.
            </p>
            <div className="mt-5">
                <GradientButton gradient="from-red-500 to-rose-500" onClick={onConnect}>
                    Connect with YouTube
                </GradientButton>
            </div>
        </GlassCard>
    );
}

export function YouTubePlaylistDetailView({ playlist, playlistItems, isLoadingItems, onBack }: YouTubePlaylistDetailViewProps) {
    return (
        <div className="grid gap-4 lg:grid-cols-[320px_1fr]">
            <GlassCard className="p-5">
                <button
                    type="button"
                    onClick={onBack}
                    className="mb-4 text-sm font-semibold text-indigo-700 hover:text-indigo-500"
                >
                    ← All playlists
                </button>
                {playlist.thumbnailUrl ? (
                    <img
                        src={playlist.thumbnailUrl}
                        alt={playlist.title}
                        className="h-52 w-full rounded-2xl object-cover"
                    />
                ) : (
                    <div className="flex h-52 w-full items-center justify-center rounded-2xl bg-white/70 text-gray-500">
                        <YouTubeIcon ariaLabel="Playlist thumbnail not available" />
                    </div>
                )}
                <h2 className="mt-4 text-xl font-semibold">{playlist.title}</h2>
                {playlist.description ? <p className="mt-2 text-sm text-gray-600">{playlist.description}</p> : null}
            </GlassCard>

            <GlassCard className="p-4">
                {isLoadingItems ? <p className="p-6 text-sm text-gray-600">Loading playlist items…</p> : <YouTubeSongList playlistItems={playlistItems} />}
            </GlassCard>
        </div>
    );
}

export function YouTubePlaylistBrowser({ status, playlists, onRefresh, onSelectPlaylist }: YouTubePlaylistBrowserProps) {
    return (
        <div className="space-y-4">
            <GlassCard className="p-5">
                <p className="text-sm text-gray-600">
                    Connected as <span className="font-semibold text-gray-900">{status.displayName ?? 'YouTube account'}</span>
                </p>
                <div className="mt-4">
                    <GradientButton tone="soft" onClick={onRefresh}>
                        Refresh playlists
                    </GradientButton>
                </div>
            </GlassCard>

            {playlists.length === 0 ? (
                <GlassCard className="p-6">
                    <p className="text-sm text-gray-600">No playlists found in this account.</p>
                </GlassCard>
            ) : (
                <section className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
                    {playlists.map((playlist) => (
                        <YouTubePlaylistCard key={playlist.id} playlist={playlist} onSelect={onSelectPlaylist} />
                    ))}
                </section>
            )}
        </div>
    );
}
