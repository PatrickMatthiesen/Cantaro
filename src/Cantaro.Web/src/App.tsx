import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { LoginForm } from './components/LoginForm';
import { RegisterForm } from './components/RegisterForm';
import { UserProfile } from './components/UserProfile';
import { SyncButton } from './components/SyncButton';
import { YouTubePlaylistsPage } from './pages/YouTubePlaylistsPage';
import { MatchingReviewPage } from './pages/MatchingReviewPage';
import { MediaPage } from './pages/MediaPage';
import { ExtensionAuthPage } from './pages/ExtensionAuthPage';
import { platformManager, type PlatformId } from './platforms';
import { platformCatalog } from './platforms/catalog';
import { GlassCard, GradientButton, PlatformTile } from './components/ui/GlassComponents';

type Page = 'home' | 'matching' | 'media' | 'extension-auth' | PlatformId;

const STATIC_PATH_PAGES = {
  '/matching': 'matching',
  '/extension-auth': 'extension-auth',
} as const;

interface LandingPageProps {
  showRegister: boolean;
  onShowRegister: () => void;
  onShowLogin: () => void;
}

interface AddPlatformMenuProps {
  menuRef: RefObject<HTMLDivElement | null>;
  isOpen: boolean;
  platformsToAdd: typeof platformCatalog;
  onToggle: () => void;
  onSelectPlatform: (platform: (typeof platformCatalog)[number]) => void;
}

interface PlatformsPanelProps {
  menuRef: RefObject<HTMLDivElement | null>;
  isCheckingConnectedAccounts: boolean;
  showAddPlatformMenu: boolean;
  connectedPlatforms: typeof platformCatalog;
  platformsToAdd: typeof platformCatalog;
  onToggleAddPlatformMenu: () => void;
  onSelectPlatform: (platform: (typeof platformCatalog)[number]) => void;
  onNavigateToPage: (page: Page) => void;
}

interface WorkspaceHomeProps {
  connectedPlatformIds: PlatformId[];
  isCheckingConnectedAccounts: boolean;
  showAddPlatformMenu: boolean;
  addPlatformMenuRef: RefObject<HTMLDivElement | null>;
  onToggleAddPlatformMenu: () => void;
  onSelectPlatform: (platform: (typeof platformCatalog)[number]) => void;
  onNavigateToPage: (page: Page) => void;
}

interface AuthenticatedPageContentProps extends WorkspaceHomeProps {
  currentPage: Page;
}

function resolvePageFromPath(path: string): Page {
  const platformPath = path.slice(1) as PlatformId;
  if (platformCatalog.some((platform) => platform.id === platformPath)) return platformPath;
  if (path === '/media' || path.startsWith('/media/')) return 'media';
  if (path in STATIC_PATH_PAGES) return STATIC_PATH_PAGES[path as keyof typeof STATIC_PATH_PAGES];

  return 'home';
}

function useWorkspaceNavigation() {
  const [currentPage, setCurrentPage] = useState<Page>(() => resolvePageFromPath(window.location.pathname));
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

  return {
    currentPage,
    showAddPlatformMenu,
    setShowAddPlatformMenu,
    addPlatformMenuRef,
    navigateTo,
  };
}

function useConnectedPlatforms(isAuthenticated: boolean, currentPage: Page) {
  const [connectedPlatformIds, setConnectedPlatformIds] = useState<PlatformId[]>([]);
  const [isCheckingConnectedAccounts, setIsCheckingConnectedAccounts] = useState(true);

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

    if (currentPage !== 'home') {
      return;
    }

    void loadConnectedAccountStatus();
  }, [currentPage, isAuthenticated, loadConnectedAccountStatus]);

  return { connectedPlatformIds, isCheckingConnectedAccounts };
}

function LoadingState() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-800">
      <GlassCard className="flex items-center gap-3 px-6 py-4">
        <span className="h-3 w-3 animate-pulse rounded-full bg-indigo-500" aria-hidden />
        <p className="text-sm font-medium">Loading…</p>
      </GlassCard>
    </div>
  );
}

function LandingPage({ showRegister, onShowRegister, onShowLogin }: LandingPageProps) {
  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto flex min-h-screen max-w-6xl flex-col px-6 pt-8 pb-16">
        <header className="mb-8">
          <p className="text-xs tracking-[0.35em] text-gray-500 uppercase">Cantaro</p>
          <h1 className="mt-1 bg-linear-to-r from-indigo-600 to-pink-600 bg-clip-text text-3xl font-bold text-transparent">
            Playlist workspace
          </h1>
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
              <RegisterForm onSwitchToLogin={onShowLogin} />
            ) : (
              <LoginForm onSwitchToRegister={onShowRegister} />
            )}
          </GlassCard>
        </main>
      </div>
    </div>
  );
}

function AddPlatformMenu({ menuRef, isOpen, platformsToAdd, onToggle, onSelectPlatform }: AddPlatformMenuProps) {
  return (
    <div ref={menuRef} className="relative ml-auto self-start">
      <GradientButton
        type="button"
        gradient="from-indigo-500 to-purple-500"
        aria-expanded={isOpen}
        aria-haspopup="menu"
        onClick={onToggle}
      >
        + Add Platform
      </GradientButton>
      {isOpen ? (
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
                onClick={() => onSelectPlatform(platform)}
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
  );
}

function PlatformsPanel({
  menuRef,
  isCheckingConnectedAccounts,
  showAddPlatformMenu,
  connectedPlatforms,
  platformsToAdd,
  onToggleAddPlatformMenu,
  onSelectPlatform,
  onNavigateToPage,
}: PlatformsPanelProps) {
  return (
    <GlassCard className="overflow-visible p-7">
      <div>
        <div className="flex w-full items-center justify-between gap-3 sm:w-auto">
          <h2 className="text-2xl font-semibold">Platforms</h2>
          <AddPlatformMenu
            menuRef={menuRef}
            isOpen={showAddPlatformMenu}
            platformsToAdd={platformsToAdd}
            onToggle={onToggleAddPlatformMenu}
            onSelectPlatform={onSelectPlatform}
          />
        </div>
        {connectedPlatforms.length === 0 && !isCheckingConnectedAccounts ? (
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
                  onNavigateToPage(platform.id);
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
  );
}

function SetupCard({ onNavigateToPage }: { onNavigateToPage: (page: Page) => void }) {
  return (
    <GlassCard className="p-7">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Service setup</p>
      <h3 className="mt-2 text-xl font-semibold">Connect your first service</h3>
      <p className="mt-1 text-sm text-gray-600">
        Start with YouTube, then add more services as they become available.
      </p>
      <div className="mt-4">
        <GradientButton gradient="from-red-500 to-rose-500" onClick={() => onNavigateToPage('youtube')}>
          Go to YouTube
        </GradientButton>
      </div>
    </GlassCard>
  );
}

function WorkflowNotesCard({ onNavigateToPage }: { onNavigateToPage: (page: Page) => void }) {
  return (
    <GlassCard className="p-6">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Workflow notes</p>
      <ul className="mt-4 space-y-3 text-sm text-gray-700">
        <li className="rounded-xl bg-white/70 px-3 py-2">Connect your platforms and sync to a single collection.</li>
        <li className="rounded-xl bg-white/70 px-3 py-2">Sync runs happen on demand and support all or selected playlists.</li>
        <li className="rounded-xl bg-white/70 px-3 py-2">When a song match is unclear, Cantaro keeps it visible for manual review.</li>
      </ul>
      <div className="mt-4">
        <GradientButton tone="soft" onClick={() => onNavigateToPage('matching')}>
          Review matching queue
        </GradientButton>
      </div>
    </GlassCard>
  );
}

function MediaTrackingCard({ onNavigateToPage }: { onNavigateToPage: (page: Page) => void }) {
  return (
    <GlassCard className="p-6">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Media tracking</p>
      <p className="mt-2 text-sm text-gray-700">
        Connect AniList to import your anime and manga library into Cantaro.
      </p>
      <div className="mt-4 flex flex-wrap gap-2">
        <GradientButton
          gradient="from-blue-500 to-cyan-500"
          onClick={() => onNavigateToPage('media')}
        >
          Manage providers
        </GradientButton>
        <GradientButton
          tone="soft"
          onClick={() => {
            onNavigateToPage('media');
            window.history.replaceState({}, '', '/media/library');
          }}
        >
          Browse library
        </GradientButton>
      </div>
    </GlassCard>
  );
}

function WorkspaceHome({
  connectedPlatformIds,
  isCheckingConnectedAccounts,
  showAddPlatformMenu,
  addPlatformMenuRef,
  onToggleAddPlatformMenu,
  onSelectPlatform,
  onNavigateToPage,
}: WorkspaceHomeProps) {
  const connectedPlatformIdSet = new Set(connectedPlatformIds);
  const hasConnectedAccounts = connectedPlatformIds.length > 0;
  const primaryConnectedPlatform = connectedPlatformIds[0] ?? 'youtube';
  const primaryConnectedPlatformDetails =
    platformCatalog.find((platform) => platform.id === primaryConnectedPlatform) ?? platformCatalog[0];
  const connectedPlatforms = platformCatalog.filter((platform) => connectedPlatformIdSet.has(platform.id));
  const platformsToAdd = platformCatalog.filter((platform) => !connectedPlatformIdSet.has(platform.id));

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto flex min-h-screen max-w-6xl flex-col px-6 pt-8 pb-16">
        <header className="mb-6">
          <p className="text-xs tracking-[0.35em] text-gray-500 uppercase">Cantaro</p>
          <h1 className="mt-1 text-3xl font-bold text-gray-900">Workspace</h1>
        </header>

        <main className="grid flex-1 gap-6 lg:grid-cols-[1.2fr_0.8fr]">
          <section className="space-y-6">
            <PlatformsPanel
              menuRef={addPlatformMenuRef}
              isCheckingConnectedAccounts={isCheckingConnectedAccounts}
              showAddPlatformMenu={showAddPlatformMenu}
              connectedPlatforms={connectedPlatforms}
              platformsToAdd={platformsToAdd}
              onToggleAddPlatformMenu={onToggleAddPlatformMenu}
              onSelectPlatform={onSelectPlatform}
              onNavigateToPage={onNavigateToPage}
            />

            {!isCheckingConnectedAccounts && hasConnectedAccounts ? (
              <SyncButton
                platformId={primaryConnectedPlatform}
                platformName={primaryConnectedPlatformDetails.name}
              />
            ) : (
              <SetupCard onNavigateToPage={onNavigateToPage} />
            )}
          </section>

          <section className="space-y-6">
            <UserProfile />
            <WorkflowNotesCard onNavigateToPage={onNavigateToPage} />
            <MediaTrackingCard onNavigateToPage={onNavigateToPage} />
          </section>
        </main>
      </div>
    </div>
  );
}

function AuthenticatedPageContent({ currentPage, onNavigateToPage, ...homeProps }: AuthenticatedPageContentProps & { onNavigateToPage: (page: Page) => void }) {
  if (currentPage === 'extension-auth') {
    return <ExtensionAuthPage />;
  }

  if (currentPage === 'youtube') {
    return <YouTubePlaylistsPage onNavigateHome={() => onNavigateToPage('home')} onNavigateMatching={() => onNavigateToPage('matching')} />;
  }

  if (currentPage === 'matching') {
    return <MatchingReviewPage onNavigateHome={() => onNavigateToPage('home')} />;
  }

  if (currentPage === 'media') {
    return <MediaPage onNavigateHome={() => onNavigateToPage('home')} />;
  }

  return <WorkspaceHome {...homeProps} onNavigateToPage={onNavigateToPage} />;
}

function AuthenticatedApp() {
  const { isAuthenticated, isLoading } = useAuth();
  const [showRegister, setShowRegister] = useState(false);
  const { currentPage, showAddPlatformMenu, setShowAddPlatformMenu, addPlatformMenuRef, navigateTo } = useWorkspaceNavigation();
  const { connectedPlatformIds, isCheckingConnectedAccounts } = useConnectedPlatforms(isAuthenticated, currentPage);

  const handleSelectPlatform = useCallback(async (platform: (typeof platformCatalog)[number]) => {
    setShowAddPlatformMenu(false);
    if (!platform.implemented) {
      return;
    }

    try {
      await platformManager.connect(platform.id, {
        route: window.location.pathname,
        trigger: 'add-platform-menu',
      });
    } catch {
      setShowAddPlatformMenu(false);
    }
  }, [setShowAddPlatformMenu]);

  if (isLoading) {
    return <LoadingState />;
  }

  if (!isAuthenticated) {
    return (
      <LandingPage
        showRegister={showRegister}
        onShowRegister={() => setShowRegister(true)}
        onShowLogin={() => setShowRegister(false)}
      />
    );
  }

  return (
    <AuthenticatedPageContent
      currentPage={currentPage}
      connectedPlatformIds={connectedPlatformIds}
      isCheckingConnectedAccounts={isCheckingConnectedAccounts}
      showAddPlatformMenu={showAddPlatformMenu}
      addPlatformMenuRef={addPlatformMenuRef}
      onToggleAddPlatformMenu={() => setShowAddPlatformMenu((previous) => !previous)}
      onSelectPlatform={handleSelectPlatform}
      onNavigateToPage={navigateTo}
    />
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
