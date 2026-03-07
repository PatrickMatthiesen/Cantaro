import { useCallback, useEffect, useRef, useState } from 'react';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { LoginForm } from './components/LoginForm';
import { RegisterForm } from './components/RegisterForm';
import { UserProfile } from './components/UserProfile';
import { SyncButton } from './components/SyncButton';
import { YouTubePlaylistsPage } from './pages/YouTubePlaylistsPage';
import { ComponentsPage } from './pages/ComponentsPage';
import { MatchingReviewPage } from './pages/MatchingReviewPage';
import { platformManager, type PlatformId } from './platforms';
import { platformCatalog } from './platforms/catalog';
import { Design1 } from './designs/Design1';
import { Design2 } from './designs/Design2';
import { Design3 } from './designs/Design3';
import { Design4 } from './designs/Design4';
import { Design5 } from './designs/Design5';
import { Design6 } from './designs/Design6';
import { Design7 } from './designs/Design7';
import { Design8 } from './designs/Design8';
import { Design9 } from './designs/Design9';
import { GlassCard, GradientButton, PlatformTile } from './components/ui/GlassComponents';

type DesignPage = '1' | '2' | '3' | '4' | '5' | '6' | '7' | '8' | '9';
type Page = 'home' | 'matching' | 'components' | DesignPage | PlatformId;

function resolvePageFromPath(path: string): Page {
  const platformPath = path.slice(1) as PlatformId;
  if (platformCatalog.some((platform) => platform.id === platformPath)) return platformPath;
  if (path === '/matching') return 'matching';
  if (path === '/components') return 'components';

  const match = path.match(/^\/([1-9])$/);
  if (match) return match[1] as DesignPage;

  return 'home';
}

function AuthenticatedApp() {
  const { isAuthenticated, isLoading } = useAuth();
  const [showRegister, setShowRegister] = useState(false);
  const [currentPage, setCurrentPage] = useState<Page>(() => resolvePageFromPath(window.location.pathname));
  const [connectedPlatformIds, setConnectedPlatformIds] = useState<PlatformId[]>([]);
  const [isCheckingConnectedAccounts, setIsCheckingConnectedAccounts] = useState(true);
  const [showAddPlatformMenu, setShowAddPlatformMenu] = useState(false);
  const addPlatformMenuRef = useRef<HTMLDivElement | null>(null);

  const navigateTo = useCallback((page: Page) => {
    setShowAddPlatformMenu(false);
    setCurrentPage(page);
    const targetPath = page === 'home' ? '/' : `/${page}`;
    window.history.pushState({}, '', targetPath);
  }, []);

  useEffect(() => {
    const handlePopState = () => setCurrentPage(resolvePageFromPath(window.location.pathname));
    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, []);

  useEffect(() => {
    const handleDocumentMouseDown = (event: MouseEvent) => {
      if (addPlatformMenuRef.current && !addPlatformMenuRef.current.contains(event.target as Node)) {
        setShowAddPlatformMenu(false);
      }
    };

    document.addEventListener('mousedown', handleDocumentMouseDown);
    return () => document.removeEventListener('mousedown', handleDocumentMouseDown);
  }, []);

  const loadConnectedAccountStatus = useCallback(async () => {
    setIsCheckingConnectedAccounts(true);
    try {
      const implementedPlatforms = platformCatalog.filter((platform) => platform.implemented);
      const statuses = await Promise.all(
        implementedPlatforms.map(async (platform) => {
          try {
            const status = await platformManager.status(platform.id);
            return status.isConnected ? platform.id : null;
          } catch {
            return null;
          }
        }),
      );

      setConnectedPlatformIds(statuses.filter((platformId): platformId is PlatformId => platformId !== null));
    } catch {
      setConnectedPlatformIds([]);
    } finally {
      setIsCheckingConnectedAccounts(false);
    }
  }, []);

  useEffect(() => {
    if (!isAuthenticated) {
      setConnectedPlatformIds([]);
      setIsCheckingConnectedAccounts(false);
      return;
    }

    if (currentPage !== 'home') return;
    loadConnectedAccountStatus();
  }, [currentPage, isAuthenticated, loadConnectedAccountStatus]);

  const connectedPlatformIdSet = new Set(connectedPlatformIds);
  const hasConnectedAccounts = connectedPlatformIds.length > 0;
  const primaryConnectedPlatform = connectedPlatformIds[0] ?? 'youtube';
  const primaryConnectedPlatformDetails =
    platformCatalog.find((platform) => platform.id === primaryConnectedPlatform) ?? platformCatalog[0];
  const connectedPlatforms = platformCatalog.filter((platform) => connectedPlatformIdSet.has(platform.id));
  const platformsToAdd = platformCatalog.filter((platform) => !connectedPlatformIdSet.has(platform.id));

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-800">
        <GlassCard className="flex items-center gap-3 px-6 py-4">
          <span className="h-3 w-3 animate-pulse rounded-full bg-indigo-500" aria-hidden />
          <p className="text-sm font-medium">Loading…</p>
        </GlassCard>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
        <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
        <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

        <div className="relative z-10 mx-auto flex min-h-screen max-w-6xl flex-col px-6 pt-8 pb-16">
          <header className="mb-8 flex items-center justify-between">
            <div>
              <p className="text-xs tracking-[0.35em] text-gray-500 uppercase">Cantaro</p>
              <h1 className="mt-1 bg-linear-to-r from-indigo-600 to-pink-600 bg-clip-text text-3xl font-bold text-transparent">
                Playlist workspace
              </h1>
            </div>
            <GradientButton tone="soft" onClick={() => navigateTo('components')}>
              Components
            </GradientButton>
          </header>

          <main className="grid flex-1 gap-6 lg:grid-cols-[1.1fr_0.9fr]">
            <GlassCard className="p-8">
              <h2 className="text-2xl font-semibold text-gray-900">Get started</h2>
              <p className="mt-2 text-sm text-gray-600">
                Connect services, sync playlists, and review conflicts when a match needs confirmation.
              </p>
              <ol className="mt-6 space-y-3">
                {[
                  'Create an account and sign in.',
                  'Connect your first service workspace (Only YouTube is currently being supported, but more are comming soon).',
                  'Run sync and manage playlist updates from Cantaro.',
                ].map((item, index) => (
                  <li key={item} className="flex items-start gap-3 rounded-2xl bg-white/70 p-4">
                    <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-linear-to-r from-indigo-500 to-purple-500 text-xs font-semibold text-white">
                      {index + 1}
                    </span>
                    <span className="text-sm text-gray-700">{item}</span>
                  </li>
                ))}
              </ol>
            </GlassCard>

            <GlassCard className="p-8">
              {showRegister ? (
                <RegisterForm onSwitchToLogin={() => setShowRegister(false)} />
              ) : (
                <LoginForm onSwitchToRegister={() => setShowRegister(true)} />
              )}
            </GlassCard>
          </main>
        </div>
      </div>
    );
  }

  if (currentPage === 'youtube') {
    return <YouTubePlaylistsPage onNavigateHome={() => navigateTo('home')} onNavigateMatching={() => navigateTo('matching')} />;
  }

  if (currentPage === 'matching') {
    return <MatchingReviewPage onNavigateHome={() => navigateTo('home')} />;
  }

  if (currentPage === 'components') {
    return <ComponentsPage />;
  }

  if (currentPage === '1') return <Design1 />;
  if (currentPage === '2') return <Design2 />;
  if (currentPage === '3') return <Design3 />;
  if (currentPage === '4') return <Design4 />;
  if (currentPage === '5') return <Design5 />;
  if (currentPage === '6') return <Design6 />;
  if (currentPage === '7') return <Design7 />;
  if (currentPage === '8') return <Design8 />;
  if (currentPage === '9') return <Design9 />;

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto flex min-h-screen max-w-6xl flex-col px-6 pt-8 pb-16">
        <header className="mb-6 flex items-center justify-between">
          <div>
            <p className="text-xs tracking-[0.35em] text-gray-500 uppercase">Cantaro</p>
            <h1 className="mt-1 text-3xl font-bold text-gray-900">Workspace</h1>
          </div>
          <GradientButton tone="soft" onClick={() => navigateTo('components')}>
            Components
          </GradientButton>
        </header>

        <main className="grid flex-1 gap-6 lg:grid-cols-[1.2fr_0.8fr]">
          <section className="space-y-6">
            <GlassCard className="overflow-visible p-7">
              <div>
                <div className="flex w-full items-center justify-between gap-3 sm:w-auto">
                  <h2 className="text-2xl font-semibold">Platforms</h2>
                  <div ref={addPlatformMenuRef} className="relative ml-auto self-start">
                    <GradientButton
                      type="button"
                      gradient="from-indigo-500 to-purple-500"
                      aria-expanded={showAddPlatformMenu}
                      aria-haspopup="menu"
                      onClick={() => setShowAddPlatformMenu((previous) => !previous)}
                    >
                      + Add Platform
                    </GradientButton>
                    {showAddPlatformMenu ? (
                      <div className="absolute right-0 z-20 mt-2 w-56 rounded-2xl border border-white/80 bg-white/95 p-2 shadow-lg backdrop-blur">
                        {platformsToAdd.length === 0 ? (
                          <p className="px-3 py-2 text-xs text-gray-500">All platforms are already added.</p>
                        ) : (
                          platformsToAdd.map((platform) => (
                            <button
                              key={platform.id}
                              type="button"
                              className="flex w-full items-center justify-between rounded-xl px-3 py-2 text-left text-sm font-medium text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-60"
                              disabled={!platform.implemented}
                              onClick={async () => {
                                if (platform.implemented) {
                                  setShowAddPlatformMenu(false);
                                  try {
                                    await platformManager.connect(platform.id, {
                                      route: window.location.pathname,
                                      trigger: 'add-platform-menu',
                                    });
                                  } catch {
                                    setShowAddPlatformMenu(false);
                                  }
                                } else {
                                  setShowAddPlatformMenu(false);
                                }
                              }}
                            >
                              <span>{platform.name}</span>
                              <span className="text-xs text-gray-500">
                                {platform.implemented ? 'Available' : 'Coming soon'}
                              </span>
                            </button>
                          ))
                        )}
                      </div>
                    ) : null}
                  </div>
                </div>
                {!hasConnectedAccounts ? (
                  <div className="mt-5 rounded-2xl bg-white/70 p-4 text-sm text-gray-600">
                    No platforms connected yet. Click "Add Platform" to connect your first service and start syncing playlists.
                  </div>
                ) : (
                  <p className="mt-2 text-sm text-gray-600">
                    Connect your music service accounts to sync playlists across platforms.
                  </p>
                )}
              </div>

              {connectedPlatforms.length > 0 ? (
                <div className="mt-5 grid gap-4 sm:grid-cols-2">
                  {connectedPlatforms.map((platform) => (
                    <PlatformTile
                      key={platform.id}
                      onClick={() => {
                        if (platform.implemented) {
                          navigateTo(platform.id);
                        }
                      }}
                      platform={{
                        name: platform.name,
                        status: 'connected',
                        tracks: 0,
                        icon: platform.icon,
                        gradient: platform.gradient,
                      }}
                    />
                  ))}
                </div>
              ) : null}

            </GlassCard>

            {!isCheckingConnectedAccounts && hasConnectedAccounts ? (
              <SyncButton
                platformId={primaryConnectedPlatform}
                platformName={primaryConnectedPlatformDetails.name}
              />
            ) : (
              <GlassCard className="p-7">
                <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Service setup</p>
                <h3 className="mt-2 text-xl font-semibold">Connect your first service</h3>
                <p className="mt-1 text-sm text-gray-600">
                  Start with YouTube, then add more services as they become available.
                </p>
                <div className="mt-4">
                  <GradientButton gradient="from-red-500 to-rose-500" onClick={() => navigateTo('youtube')}>
                    Go to YouTube
                  </GradientButton>
                </div>
              </GlassCard>
            )}
          </section>

          <section className="space-y-6">
            <UserProfile />
              <GlassCard className="p-6">
                <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Workflow notes</p>
                <ul className="mt-4 space-y-3 text-sm text-gray-700">
                  <li className="rounded-xl bg-white/70 px-3 py-2">Connect your platforms and sync to a single collection.</li>
                  <li className="rounded-xl bg-white/70 px-3 py-2">Sync runs happen on demand and support all or selected playlists.</li>
                  <li className="rounded-xl bg-white/70 px-3 py-2">When a song match is unclear, Cantaro keeps it visible for manual review.</li>
                </ul>
                <div className="mt-4">
                  <GradientButton tone="soft" onClick={() => navigateTo('matching')}>
                    Review matching queue
                  </GradientButton>
                </div>
              </GlassCard>
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
