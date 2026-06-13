import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { Link } from '@tanstack/react-router';
import { platformCatalog, platformManager, SyncButton, type PlatformId } from '@cantaro/client-shared/music';
import { GlassCard, GradientButton, StatusBadge } from '@cantaro/client-shared/ui';
import { useConnectedMusicPlatforms } from './useConnectedMusicPlatforms';
import { MusicPageShell } from './MusicPageShell';

interface AddPlatformMenuProps {
  menuRef: RefObject<HTMLDivElement | null>;
  isOpen: boolean;
  platformsToAdd: typeof platformCatalog;
  onToggle: () => void;
  onSelectPlatform: (platform: (typeof platformCatalog)[number]) => void;
}

interface PlatformTileProps {
  platform: {
    id: PlatformId;
    name: string;
    status: 'connected' | 'available' | 'warning';
    tracks: number;
    icon: string;
    gradient: string;
  };
}

function platformLinkButtonClassName({
  gradient = 'from-indigo-500 to-purple-500',
  tone,
}: {
  gradient?: string;
  tone?: 'soft';
}) {
  const toneClass = tone === 'soft'
    ? 'bg-white/70 text-gray-800 hover:bg-mist-100'
    : `bg-linear-to-r ${gradient} text-white hover:brightness-105`;

  return `inline-flex items-center justify-center rounded-2xl px-5 py-3 text-sm font-semibold transition-all hover:scale-[1.03] ${toneClass}`;
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

function PlatformTile({ platform }: PlatformTileProps) {
  return (
    <Link to="/music/platforms/$platformId" params={{ platformId: platform.id }} className="block w-full text-left">
      <GlassCard
        interactive
        hoverGradient={platform.gradient}
        className="group p-4 transition"
      >
        <div className="flex items-start gap-3">
          <div className={`flex aspect-3/2 h-12 w-16 items-center justify-center rounded-xl bg-linear-to-br ${platform.gradient} text-white`}>
            {platform.icon}
          </div>
          <div className="w-full">
            <p className="font-semibold text-gray-800">{platform.name}</p>
            <div className="mt-1 flex items-center justify-between gap-2">
              <p className="text-xs text-gray-500">{platform.tracks.toLocaleString()} tracks</p>
              <StatusBadge status={platform.status} />
            </div>
          </div>
        </div>
      </GlassCard>
    </Link>
  );
}

function PlatformsPanel({
  menuRef,
  isCheckingConnectedAccounts,
  showAddPlatformMenu,
  connectedPlatformIds,
  onToggleAddPlatformMenu,
  onSelectPlatform,
}: {
  menuRef: RefObject<HTMLDivElement | null>;
  isCheckingConnectedAccounts: boolean;
  showAddPlatformMenu: boolean;
  connectedPlatformIds: PlatformId[];
  onToggleAddPlatformMenu: () => void;
  onSelectPlatform: (platform: (typeof platformCatalog)[number]) => void;
}) {
  const connectedPlatformIdSet = new Set(connectedPlatformIds);
  const connectedPlatforms = platformCatalog.filter((platform) => connectedPlatformIdSet.has(platform.id));
  const platformsToAdd = platformCatalog.filter((platform) => !connectedPlatformIdSet.has(platform.id));

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
              platform={{
                id: platform.id,
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

function SetupCard() {
  return (
    <GlassCard className="p-7">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Service setup</p>
      <h3 className="mt-2 text-xl font-semibold">Connect your first service</h3>
      <p className="mt-1 text-sm text-gray-600">
        Start with YouTube, then add more services as they become available.
      </p>
      <div className="mt-4">
        <Link
          to="/music/platforms/$platformId"
          params={{ platformId: 'youtube' }}
          className={platformLinkButtonClassName({ gradient: 'from-red-500 to-rose-500' })}
        >
          Go to YouTube
        </Link>
      </div>
    </GlassCard>
  );
}

function WorkflowNotesCard() {
  return (
    <GlassCard className="p-6">
      <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Workflow notes</p>
      <ul className="mt-4 space-y-3 text-sm text-gray-700">
        <li className="rounded-xl bg-white/70 px-3 py-2">Connect your platforms and sync to a single collection.</li>
        <li className="rounded-xl bg-white/70 px-3 py-2">Sync runs happen on demand and support all or selected playlists.</li>
        <li className="rounded-xl bg-white/70 px-3 py-2">When a song match is unclear, Cantaro keeps it visible for manual review.</li>
      </ul>
      <div className="mt-4">
        <Link to="/music/matching" className={platformLinkButtonClassName({ tone: 'soft' })}>
          Review matching queue
        </Link>
      </div>
    </GlassCard>
  );
}

export function MusicPlatformsPage() {
  const [showAddPlatformMenu, setShowAddPlatformMenu] = useState(false);
  const addPlatformMenuRef = useRef<HTMLDivElement | null>(null);
  const { connectedPlatformIds, isCheckingConnectedAccounts } = useConnectedMusicPlatforms();
  const hasConnectedAccounts = connectedPlatformIds.length > 0;
  const primaryConnectedPlatform = connectedPlatformIds[0] ?? 'youtube';
  const primaryConnectedPlatformDetails =
    platformCatalog.find((platform) => platform.id === primaryConnectedPlatform) ?? platformCatalog[0];

  useEffect(() => {
    const handleDocumentMouseDown = (event: MouseEvent) => {
      if (addPlatformMenuRef.current && !addPlatformMenuRef.current.contains(event.target as Node)) {
        setShowAddPlatformMenu(false);
      }
    };

    document.addEventListener('mousedown', handleDocumentMouseDown);
    return () => document.removeEventListener('mousedown', handleDocumentMouseDown);
  }, []);

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
  }, []);

  return (
    <MusicPageShell>
      <div className="space-y-6">
        <header>
          <p className="text-xs font-black tracking-[0.22em] text-violet-600 uppercase">Music</p>
          <h1 className="mt-2 text-4xl font-black text-slate-950">Platforms</h1>
          <p className="mt-2 max-w-2xl text-sm leading-6 font-semibold text-slate-500">
            Connect accounts, review platform status, and run playlist sync from one place.
          </p>
        </header>

        <main className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
          <section className="space-y-6">
            <PlatformsPanel
              menuRef={addPlatformMenuRef}
              isCheckingConnectedAccounts={isCheckingConnectedAccounts}
              showAddPlatformMenu={showAddPlatformMenu}
              connectedPlatformIds={connectedPlatformIds}
              onToggleAddPlatformMenu={() => setShowAddPlatformMenu((previous) => !previous)}
              onSelectPlatform={handleSelectPlatform}
            />

            {!isCheckingConnectedAccounts && hasConnectedAccounts ? (
              <SyncButton
                platformId={primaryConnectedPlatform}
                platformName={primaryConnectedPlatformDetails.name}
              />
            ) : (
              <SetupCard />
            )}
          </section>

          <WorkflowNotesCard />
        </main>
      </div>
    </MusicPageShell>
  );
}
