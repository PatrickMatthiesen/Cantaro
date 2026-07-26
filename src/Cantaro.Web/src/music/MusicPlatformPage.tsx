import { platformCatalog } from '@cantaro/client-shared/music';
import { GlassCard } from '@cantaro/client-shared/ui';
import { MusicPageShell } from './MusicPageShell';
import { SpotifyMusicPlatformPage } from './SpotifyMusicPlatformPage';
import { YouTubeMusicPlatformPage } from './YouTubeMusicPlatformPage';

export function MusicPlatformSurface({ platformId, playlistId = null }: { platformId: string; playlistId?: string | null }) {
  if (platformId === 'youtube') {
    return <YouTubeMusicPlatformPage playlistId={playlistId} />;
  }

  if (platformId === 'spotify') {
    return <SpotifyMusicPlatformPage playlistId={playlistId} />;
  }

  const platform = platformCatalog.find((item) => item.id === platformId);

  return (
    <MusicPageShell>
      <GlassCard className="p-6">
        <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Platform</p>
        <h2 className="mt-2 text-2xl font-semibold text-gray-900">{platform?.name ?? platformId}</h2>
        <p className="mt-2 text-sm text-gray-600">This platform is not available yet.</p>
      </GlassCard>
    </MusicPageShell>
  );
}
