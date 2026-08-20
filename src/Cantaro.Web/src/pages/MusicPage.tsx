import { Outlet } from '@tanstack/react-router';
import type { PlatformId } from '@cantaro/client-shared/music';
import { RequireAuth } from '../components/AppShell';
import { MatchingReviewPage } from './MatchingReviewPage';
import { MusicHomeDashboard } from '../music/MusicHomeDashboard';
import { MusicLibraryPanel } from '../music/MusicLibraryPanel';
import { MusicLibraryProvider } from '../music/MusicLibraryContext';
import { MusicPageShell } from '../music/MusicPageShell';
import { MusicPlatformSurface } from '../music/MusicPlatformPage';
import { MusicPlaylistSyncSetupPage } from '../music/MusicPlaylistSyncSetupPage';
import { MusicPlaylistDetailPage } from '../music/MusicPlaylistDetailPage';
import { MusicPlaylistsDirectory } from '../music/MusicPlaylistsDirectory';
import { MusicPlatformsPage } from '../music/MusicPlatformsPage';
import { MusicSongDetailPage } from '../music/MusicSongPages';
import { useConnectedMusicPlatforms } from '../music/useConnectedMusicPlatforms';

export function MusicLayout() {
  return (
    <RequireAuth>
      <MusicLibraryProvider>
        <Outlet />
      </MusicLibraryProvider>
    </RequireAuth>
  );
}

export function MusicSongsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <MusicHomeDashboard library={library} />}
    </MusicLibraryPanel>
  );
}

export function MusicSongPage({ songId }: { songId: string }) {
  return (
    <MusicLibraryPanel>
      {(library) => <MusicSongDetailPage library={library} songId={songId} />}
    </MusicLibraryPanel>
  );
}

export function MusicRootPage() {
  const { connectedPlatformIds, isCheckingConnectedAccounts } = useConnectedMusicPlatforms();

  if (isCheckingConnectedAccounts) {
    return (
      <MusicPageShell>
        <div className="flex min-h-[360px] items-center justify-center border-y border-border-subtle bg-surface">
          <div className="flex items-center gap-3 text-sm font-semibold text-content-muted">
            <span className="h-2.5 w-2.5 animate-pulse rounded-full bg-personal-accent" aria-hidden />
            Checking connected platforms
          </div>
        </div>
      </MusicPageShell>
    );
  }

  return connectedPlatformIds.length > 0 ? <MusicSongsPage /> : <MusicPlatformsPage />;
}

export function MusicPlaylistsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <MusicPlaylistsDirectory library={library} />}
    </MusicLibraryPanel>
  );
}

export function MusicPlaylistPage({ playlistId }: { playlistId: string }) {
  return (
    <MusicLibraryPanel>
      {(library) => <MusicPlaylistDetailPage library={library} playlistId={playlistId} />}
    </MusicLibraryPanel>
  );
}

export function MusicMatchingPage() {
  return (
    <MusicPageShell>
      <MatchingReviewPage embedded />
    </MusicPageShell>
  );
}

export function MusicPlatformPage({ platformId, playlistId = null }: { platformId: string; playlistId?: string | null }) {
  return <MusicPlatformSurface platformId={platformId} playlistId={playlistId} />;
}

export function MusicPlatformSyncSetupPage({ initialSourcePlatformId }: { initialSourcePlatformId?: PlatformId }) {
  return <MusicPlaylistSyncSetupPage initialSourcePlatformId={initialSourcePlatformId} />;
}
