import { useCallback, useEffect, useState } from 'react';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { LoginForm } from './components/LoginForm';
import { RegisterForm } from './components/RegisterForm';
import { UserProfile } from './components/UserProfile';
import { SyncButton } from './components/SyncButton';
import { YouTubePlaylistsPage } from './pages/YouTubePlaylistsPage';
import { youtubeApi } from './services/youtubeApi';

type Page = 'home' | 'youtube';

function AuthenticatedApp() {
  const { isAuthenticated, isLoading, user } = useAuth();
  const [showRegister, setShowRegister] = useState(false);
  const [currentPage, setCurrentPage] = useState<Page>('home');
  const [hasConnectedAccounts, setHasConnectedAccounts] = useState(false);
  const [isCheckingConnectedAccounts, setIsCheckingConnectedAccounts] = useState(true);

  const loadConnectedAccountStatus = useCallback(async () => {
    setIsCheckingConnectedAccounts(true);
    try {
      const status = await youtubeApi.getStatus();
      setHasConnectedAccounts(status.isConnected);
    } catch {
      setHasConnectedAccounts(false);
    } finally {
      setIsCheckingConnectedAccounts(false);
    }
  }, []);

  useEffect(() => {
    if (!isAuthenticated) {
      setHasConnectedAccounts(false);
      setIsCheckingConnectedAccounts(false);
      return;
    }

    if (currentPage !== 'home') {
      return;
    }

    loadConnectedAccountStatus();
  }, [currentPage, isAuthenticated, loadConnectedAccountStatus]);

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-950 text-slate-200">
        <div className="flex items-center gap-4 rounded-3xl border border-white/10 bg-slate-900/60 px-8 py-6 backdrop-blur-xl">
          <span className="h-3 w-3 animate-pulse rounded-full bg-emerald-400" aria-hidden />
          <p className="text-lg font-medium tracking-tight">Preparing your Cantaro workspace…</p>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top,_rgba(99,102,241,0.25),_transparent_45%),_radial-gradient(circle_at_80%_10%,_rgba(236,72,153,0.25),_transparent_45%),_#020617]" aria-hidden />
        <div className="absolute -left-32 top-10 h-80 w-80 rounded-full bg-brand-500/20 blur-[140px]" aria-hidden />
        <div className="absolute -right-10 bottom-0 h-[420px] w-[420px] rounded-full bg-rose-500/10 blur-[180px]" aria-hidden />

        <div className="relative z-10 flex min-h-screen flex-col">
          <header className="px-6 py-6">
            <div className="mx-auto flex w-full max-w-6xl items-center justify-between">
              <div className="flex items-center gap-3">
                <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-white/10 text-lg font-semibold tracking-tight text-white">
                  CT
                </div>
                <div>
                  <p className="text-xs uppercase tracking-[0.5em] text-slate-400">Cantaro</p>
                  <p className="text-base font-semibold text-white">Unified playlist intelligence</p>
                </div>
              </div>
              <div className="flex items-center gap-3 text-xs uppercase tracking-[0.4em] text-slate-400">
                <span>OAuth · PKCE</span>
                <span className="hidden sm:inline">Encrypted refresh tokens</span>
              </div>
            </div>
          </header>

          <main className="mx-auto grid w-full max-w-6xl flex-1 gap-10 px-6 pb-16 pt-6 lg:grid-cols-[1.1fr_0.9fr]">
            <section className="glass-panel p-10 text-left">
              <p className="text-xs uppercase tracking-[0.6em] text-slate-400">Cross-platform orchestration</p>
              <h1 className="mt-5 text-4xl font-semibold leading-tight text-white sm:text-5xl">
                Stay in sync across YouTube, Spotify, and beyond.
              </h1>
              <p className="mt-6 max-w-xl text-lg text-slate-300">
                Cantaro keeps every playlist tied to a canonical TrackID so mappings, propagation, and conflict handling stay predictable—even when services disagree on metadata.
              </p>
              <div className="mt-10 grid gap-6 sm:grid-cols-2">
                {[{
                  title: 'Track identity graph',
                  copy: 'Resolve MBID/ISRC collisions, surface ambiguities, and see confidence at a glance.'
                }, {
                  title: 'Background workers',
                  copy: 'Adapters poll, back off, and retry outside the request path while you keep browsing.'
                }, {
                  title: 'Secure tokens',
                  copy: 'Refresh tokens live server-side only, encrypted at rest with rotating keys.'
                }, {
                  title: 'Self-host friendly',
                  copy: 'Run everything through Aspire locally with PostgreSQL plus the React frontend.'
                }].map((item) => (
                  <article key={item.title} className="rounded-2xl border border-white/10 bg-slate-950/30 p-5">
                    <h3 className="text-base font-semibold text-white">{item.title}</h3>
                    <p className="mt-3 text-sm text-slate-300">{item.copy}</p>
                  </article>
                ))}
              </div>
              <div className="mt-10 flex flex-wrap gap-4 text-xs font-semibold uppercase tracking-[0.3em] text-slate-400">
                {['Secure OAuth', 'Aspire-powered dev stack', 'No DRM or scraping'].map((badge) => (
                  <span key={badge} className="rounded-full border border-white/15 px-4 py-2">
                    {badge}
                  </span>
                ))}
              </div>
            </section>

            <section className="glass-panel p-8 shadow-[0_40px_140px_rgba(2,6,23,0.65)]">
              {showRegister ? (
                <RegisterForm onSwitchToLogin={() => setShowRegister(false)} />
              ) : (
                <LoginForm onSwitchToRegister={() => setShowRegister(true)} />
              )}
              <div className="mt-6 rounded-2xl border border-white/5 bg-white/5 px-4 py-3 text-center text-xs leading-5 text-slate-400">
                By continuing you agree to Cantaro storing encrypted refresh tokens server-side only.
              </div>
            </section>
          </main>
        </div>
      </div>
    );
  }

  if (currentPage === 'youtube') {
    return <YouTubePlaylistsPage onNavigateHome={() => setCurrentPage('home')} />;
  }

  return (
    <div className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_top,_rgba(99,102,241,0.2),_transparent_55%),_radial-gradient(circle_at_80%_10%,_rgba(236,72,153,0.18),_transparent_55%),_#010b1f]" aria-hidden />
      <div className="absolute -right-10 top-10 h-72 w-72 rounded-full bg-brand-400/30 blur-[160px]" aria-hidden />
      <div className="relative z-10 flex min-h-screen flex-col">
        <header className="px-6 py-6">
          <div className="mx-auto flex w-full max-w-6xl items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-white/10 text-lg font-semibold tracking-tight text-white">
                CT
              </div>
              <div>
                <p className="text-xs uppercase tracking-[0.5em] text-slate-400">Cantaro</p>
                <p className="text-base font-semibold text-white">Playlist control center</p>
              </div>
            </div>
            <button
              onClick={() => setCurrentPage('youtube')}
              className="inline-flex items-center gap-2 rounded-full border border-white/15 px-5 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-200 transition hover:border-white/40"
            >
              <span className="text-lg">⟶</span>
              <span>YouTube view</span>
            </button>
          </div>
        </header>

        <main className="mx-auto flex w-full max-w-6xl flex-1 flex-col gap-10 px-6 pb-16 pt-6">
          <section className="grid gap-8 lg:grid-cols-[1.2fr_0.8fr]">
            <article className="glass-panel p-10">
              <p className="text-xs uppercase tracking-[0.6em] text-slate-400">Overview</p>
              <h1 className="mt-4 text-4xl font-semibold text-white sm:text-5xl">
                Welcome back{user?.email ? `, ${user.email.split('@')[0]}` : ''}.
              </h1>
              <p className="mt-4 max-w-2xl text-lg text-slate-300">
                Every TrackID, playlist entry, and adapter handshake flows through Cantaro so propagation stays deterministic—even across aggressive rate limits.
              </p>
              <dl className="mt-8 grid gap-6 sm:grid-cols-3">
                {[{
                  label: 'Active playlists',
                  value: 'Smart syncing',
                  helper: 'Workers poll quietly in the background.'
                }, {
                  label: 'Track identity coverage',
                  value: 'MBID + ISRC',
                  helper: 'Heuristics handle tricky edge cases.'
                }, {
                  label: 'Local-first tooling',
                  value: 'Aspire stack',
                  helper: 'API + DB + frontend with one command.'
                }].map((item) => (
                  <div key={item.label} className="rounded-2xl border border-white/10 bg-white/5 p-5">
                    <dt className="text-xs uppercase tracking-[0.5em] text-slate-400">{item.label}</dt>
                    <dd className="mt-3 text-lg font-semibold text-white">{item.value}</dd>
                    <p className="mt-2 text-sm text-slate-400">{item.helper}</p>
                  </div>
                ))}
              </dl>
            </article>
            <UserProfile />
          </section>

          {!isCheckingConnectedAccounts && hasConnectedAccounts && <SyncButton />}

          <section className="grid gap-6 md:grid-cols-2">
            <article className="glass-panel flex flex-col justify-between bg-gradient-to-br from-red-500/30 via-rose-500/20 to-orange-400/10 p-8 text-left shadow-[0_30px_80px_rgba(244,63,94,0.25)]">
              <div>
                <p className="text-xs uppercase tracking-[0.5em] text-white/70">Connected services</p>
                <h2 className="mt-3 text-2xl font-semibold text-white">Bring YouTube playlists into Cantaro</h2>
                <p className="mt-4 text-sm text-white/80">
                  OAuth with PKCE, encrypted refresh tokens, and adapter-level mapping into TrackIDs keep your catalog consistent.
                </p>
              </div>
              <button
                onClick={() => setCurrentPage('youtube')}
                className="mt-8 inline-flex items-center justify-center gap-3 rounded-2xl bg-white/15 px-4 py-3 text-sm font-semibold text-white backdrop-blur">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden>
                  <path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z" />
                </svg>
                Launch YouTube workspace
              </button>
            </article>
            <article className="glass-panel p-8">
              <p className="text-xs uppercase tracking-[0.5em] text-slate-400">Sync telemetry</p>
              <h2 className="mt-3 text-2xl font-semibold text-white">Rate limit aware scheduling</h2>
              <p className="mt-4 text-sm text-slate-300">
                Background workers track retries, back-pressure, and conflict resolutions. When ambiguity exists, Cantaro keeps the entry flagged until you confirm the mapping.
              </p>
              <ul className="mt-6 space-y-4 text-sm text-slate-300">
                {['Deterministic TrackID-first propagation', 'Explicit "no match" + "ambiguous" states', 'Adapter isolation keeps third-party specifics contained'].map((item) => (
                  <li key={item} className="flex items-center gap-3">
                    <span className="h-2 w-2 rounded-full bg-emerald-400" aria-hidden />
                    {item}
                  </li>
                ))}
              </ul>
            </article>
          </section>
        </main>
      </div>
    </div>
  );
}

function App() {
  return (
    <AuthProvider>
      <AuthenticatedApp />
    </AuthProvider>
  );
}

export default App;
