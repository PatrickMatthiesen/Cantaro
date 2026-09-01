import { GradientButton } from '@cantaro/client-shared/ui';
import type { ActiveTabContextState } from '../shell/extensionAppTypes';
import { MusicResults } from './MusicResults';
import { MusicState } from './MusicState';
import { SongDetail } from './SongDetail';
import { useMusicPage } from './useMusicPage';

interface MusicPageProps {
  configured: boolean;
  isSigningIn: boolean;
  activeTabContext: ActiveTabContextState;
  onSignIn: () => Promise<boolean>;
}

export function MusicPage(props: MusicPageProps) {
  if (!props.configured) {
    return (
      <MusicState title="Your music, in reach" detail="Sign in to search Cantaro and recognize the song playing in this tab.">
        <GradientButton className="mt-4" onClick={() => void props.onSignIn()} disabled={props.isSigningIn}>
          {props.isSigningIn ? 'Signing in…' : 'Sign in'}
        </GradientButton>
      </MusicState>
    );
  }
  return <ConfiguredMusicPage />;
}

function ConfiguredMusicPage() {
  const page = useMusicPage();
  if (page.error) return <MusicState title="Music is unavailable" detail={page.error} />;
  if (!page.library) return <MusicState title="Loading your library…" detail="Fetching canonical songs and playlists." />;
  if (page.route.kind === 'song') {
    return (
      <SongDetail
        song={page.route.song}
        playlists={page.library.playlists}
        onBack={() => page.setRoute({ kind: 'home' })}
      />
    );
  }

  return (
    <section className="space-y-3 p-1" aria-label="Music">
      <label className="block">
        <span className="sr-only">Search music</span>
        <input
          value={page.query}
          onChange={(event) => page.setQuery(event.target.value)}
          placeholder="Search songs, artists, albums…"
          className="min-h-11 w-full border border-border-subtle bg-surface px-3 text-sm text-content outline-none placeholder:text-content-muted focus:border-focus"
        />
      </label>
      <MusicResults
        songs={page.results}
        hasLibrarySongs={page.library.songs.length > 0}
        onSelect={(song) => page.setRoute({ kind: 'song', song })}
      />
    </section>
  );
}
