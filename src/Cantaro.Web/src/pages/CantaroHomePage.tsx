import { Link } from '@tanstack/react-router';
import { GlassCard } from '@cantaro/client-shared/ui';
import { UserProfile } from '../components/UserProfile';
import { AppPageShell, GlobalHeader, RequireAuth } from '../components/AppShell';

function homeLinkButtonClassName({
  gradient = 'from-indigo-500 to-purple-500',
  tone,
}: {
  gradient?: string;
  tone?: 'soft';
} = {}) {
  const toneClass = tone === 'soft'
    ? 'bg-white/70 text-gray-800 hover:bg-mist-100'
    : `bg-linear-to-r ${gradient} text-white hover:brightness-105`;

  return `inline-flex items-center justify-center rounded-2xl px-5 py-3 text-sm font-semibold transition-all hover:scale-[1.03] ${toneClass}`;
}

function MusicPlatformsCard() {
  return (
    <GlassCard className="p-7">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Music platforms</p>
      <h2 className="mt-2 text-2xl font-semibold">Manage connections and sync</h2>
      <p className="mt-2 max-w-2xl text-sm leading-6 text-gray-600">
        Connect service accounts, review platform status, and run playlist sync from the music platforms page.
      </p>
      <div className="mt-5 flex flex-wrap gap-2">
        <Link to="/music/platforms" className={homeLinkButtonClassName()}>
          Open platforms
        </Link>
        <Link to="/music/platforms/$platformId" params={{ platformId: 'youtube' }} className={homeLinkButtonClassName({ tone: 'soft' })}>
          YouTube
        </Link>
      </div>
    </GlassCard>
  );
}

function WorkflowNotesCard() {
  return (
    <GlassCard className="p-6">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Library attention</p>
      <h2 className="mt-2 text-xl font-semibold">Review uncertain matches</h2>
      <p className="mt-2 text-sm leading-6 text-gray-700">
        Cantaro keeps unclear song matches visible so you can resolve them before they pollute the library.
      </p>
      <div className="mt-4">
        <Link to="/music/matching" className={homeLinkButtonClassName({ tone: 'soft' })}>
          Review matching queue
        </Link>
      </div>
    </GlassCard>
  );
}

function MediaTrackingCard() {
  return (
    <GlassCard className="p-6">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Media tracking</p>
      <p className="mt-2 text-sm text-gray-700">
        Connect AniList to import your anime and manga library into Cantaro.
      </p>
      <div className="mt-4 flex flex-wrap gap-2">
        <Link
          to="/media/providers"
          className={homeLinkButtonClassName({ gradient: 'from-blue-500 to-cyan-500' })}
        >
          Manage providers
        </Link>
        <Link to="/media/library" className={homeLinkButtonClassName({ tone: 'soft' })}>
          Browse library
        </Link>
      </div>
    </GlassCard>
  );
}

function CantaroHomeContent() {
  return (
    <AppPageShell>
      <GlobalHeader heading={{ eyebrow: 'Cantaro', title: 'Home' }} />

      <main className="grid flex-1 gap-6 lg:grid-cols-[1.2fr_0.8fr]">
        <section className="space-y-6">
          <MusicPlatformsCard />
          <WorkflowNotesCard />
        </section>

        <section className="space-y-6">
          <UserProfile />
          <MediaTrackingCard />
        </section>
      </main>
    </AppPageShell>
  );
}

export function CantaroHomePage() {
  return (
    <RequireAuth>
      <CantaroHomeContent />
    </RequireAuth>
  );
}
