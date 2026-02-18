import type { ReactNode } from 'react';
import { DesignNav } from '../components/DesignNav';

export type PlatformStatus = 'connected' | 'available';

export interface PlatformCard {
  id: string;
  name: string;
  status: PlatformStatus;
  tracks: number;
  icon: string;
  gradient: string;
}

export const platformCatalog: PlatformCard[] = [
  { id: 'youtube', name: 'YouTube Music', status: 'connected', tracks: 1247, icon: '▶', gradient: 'from-red-500 to-pink-500' },
  { id: 'spotify', name: 'Spotify', status: 'available', tracks: 0, icon: '♫', gradient: 'from-green-400 to-emerald-600' },
  { id: 'apple', name: 'Apple Music', status: 'available', tracks: 0, icon: '◉', gradient: 'from-pink-400 to-rose-500' },
  { id: 'tidal', name: 'Tidal', status: 'available', tracks: 0, icon: '◈', gradient: 'from-gray-700 to-gray-900' },
];

interface LayoutShellProps {
  currentDesign: string;
  userEmail?: string;
  children: ReactNode;
  sidebarTitle?: string;
  sidebarSubtitle?: string;
}

export function LayoutShell({
  currentDesign,
  userEmail,
  children,
  sidebarTitle = 'Cantaro',
  sidebarSubtitle = 'Welcome',
}: LayoutShellProps) {
  return (
    <div className="relative min-h-screen overflow-hidden bg-gradient-to-br from-indigo-50 via-purple-50 to-pink-50">
      <DesignNav currentDesign={currentDesign} style="light" />
      <div
        className="absolute -left-20 -top-20 h-80 w-80 animate-pulse rounded-full bg-gradient-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl"
        style={{ animationDuration: '8s' }}
      />
      <div
        className="absolute -bottom-40 -right-20 h-96 w-96 animate-pulse rounded-full bg-gradient-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl"
        style={{ animationDuration: '10s', animationDelay: '2s' }}
      />
      <div
        className="absolute left-1/2 top-1/2 h-72 w-72 -translate-x-1/2 -translate-y-1/2 animate-pulse rounded-full bg-gradient-to-br from-violet-300 to-fuchsia-300 opacity-20 blur-3xl"
        style={{ animationDuration: '12s', animationDelay: '4s' }}
      />

      <div className="relative z-10 flex min-h-screen">
        <aside className="w-80 p-6">
          <div className="glass-card sticky top-6 p-6">
            <div className="mb-8">
              <h1 className="bg-gradient-to-r from-indigo-600 via-purple-600 to-pink-600 bg-clip-text text-3xl font-bold text-transparent">
                {sidebarTitle}
              </h1>
              <p className="mt-1 text-xs text-gray-600">{userEmail || sidebarSubtitle}</p>
            </div>

            <nav className="space-y-2">
              {[
                { label: 'Dashboard', icon: '◆', active: true },
                { label: 'Platforms', icon: '◈', active: false },
                { label: 'Playlists', icon: '♫', active: false },
                { label: 'Analytics', icon: '◉', active: false },
                { label: 'Settings', icon: '⚙', active: false },
              ].map((item) => (
                <button
                  key={item.label}
                  className={`w-full rounded-2xl px-4 py-3 text-left text-sm font-semibold transition-all ${
                    item.active
                      ? 'bg-gradient-to-r from-indigo-500 to-purple-500 text-white shadow-lg'
                      : 'text-gray-600 hover:bg-white/50'
                  }`}
                >
                  <span className="mr-3">{item.icon}</span>
                  {item.label}
                </button>
              ))}
            </nav>

            <div className="mt-8 space-y-3">
              {[
                { label: 'Total Tracks', value: '1,247' },
                { label: 'Active Syncs', value: '1' },
                { label: 'Sync Health', value: '98%' },
              ].map((stat) => (
                <div key={stat.label} className="rounded-xl bg-white/50 px-4 py-3">
                  <div className="text-xs font-medium text-gray-500">{stat.label}</div>
                  <div className="mt-1 text-2xl font-bold text-gray-800">{stat.value}</div>
                </div>
              ))}
            </div>
          </div>
        </aside>

        <main className="flex-1 p-6 pb-28">{children}</main>
      </div>

      <style>{`
        .glass-card {
          background: rgba(255, 255, 255, 0.7);
          backdrop-filter: blur(20px);
          border-radius: 24px;
          border: 1px solid rgba(255, 255, 255, 0.8);
          box-shadow:
            0 8px 32px rgba(0, 0, 0, 0.06),
            inset 0 1px 0 rgba(255, 255, 255, 0.9);
        }
      `}</style>
    </div>
  );
}
