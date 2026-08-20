import { platformCatalog } from '@cantaro/client-shared/music';
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
      <section className="border-y border-border-subtle py-8">
        <p className="text-xs tracking-[0.24em] text-content-muted uppercase">Platform</p>
        <h2 className="mt-2 text-2xl font-semibold text-content">{platform?.name ?? platformId}</h2>
        <p className="mt-2 text-sm text-content-muted">This platform is not available yet.</p>
      </section>
    </MusicPageShell>
  );
}
