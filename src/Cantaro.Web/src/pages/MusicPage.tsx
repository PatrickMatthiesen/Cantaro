import { Outlet } from '@tanstack/react-router';
import { StatusBadge } from '@cantaro/client-shared/ui';
import { RequireAuth } from '../components/AppShell';
import { MatchingReviewPage } from './MatchingReviewPage';
import { MusicHomeDashboard } from '../music/MusicHomeDashboard';
import { MusicLibraryPanel } from '../music/MusicLibraryPanel';
import { MusicPlatformSurface } from '../music/MusicPlatformPage';
import { MusicPageShell } from '../music/MusicPageShell';
import { MusicPlaylistsDirectory } from '../music/MusicPlaylistsDirectory';

export function MusicLayout() {
  return (
    <RequireAuth>
      <Outlet />
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

export function MusicPlaylistsPage() {
  return (
    <MusicLibraryPanel>
      {(library) => <MusicPlaylistsDirectory library={library} />}
    </MusicLibraryPanel>
  );
}

export function MusicMatchingPage() {
  return (
    <MusicPageShell>
      <div className="mb-5 flex items-center gap-2">
        <StatusBadge status="warning" />
        <h1 className="text-2xl font-black text-gray-900">Library attention</h1>
      </div>
      <MatchingReviewPage embedded />
    </MusicPageShell>
  );
}

export function MusicPlatformPage({ platformId, playlistId = null }: { platformId: string; playlistId?: string | null }) {
  return <MusicPlatformSurface platformId={platformId} playlistId={playlistId} />;
}
