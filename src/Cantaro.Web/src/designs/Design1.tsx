import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { DesignNav } from '../components/DesignNav';
import { platformCatalog } from './constants';

// Design 1: "Sidebar Dashboard" with Design 2 aesthetics
// Layout: Left sidebar navigation with main content area - classic dashboard pattern

export function Design1() {
  const { user } = useAuth();
  const platforms = useMemo(() => platformCatalog, []);

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50">
      <DesignNav currentDesign="1" style="light" />
      {/* Animated background orbs */}
      <div className="absolute -top-20 -left-20 h-80 w-80 animate-pulse rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" style={{ animationDuration: '8s' }} />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 animate-pulse rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" style={{ animationDuration: '10s', animationDelay: '2s' }} />

      <div className="relative z-10 flex min-h-screen">
        {/* Sidebar */}
        <aside className="w-80 p-6">
          <div className="glass-card sticky top-6 p-6">
            <div className="mb-8">
              <h1 className="bg-linear-to-r from-indigo-600 via-purple-600 to-pink-600 bg-clip-text text-3xl font-bold text-transparent">
                Cantaro
              </h1>
              <p className="mt-1 text-xs text-gray-600">{user?.email || 'Welcome'}</p>
            </div>

            {/* Navigation */}
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
                      ? 'bg-linear-to-r from-indigo-500 to-purple-500 text-white shadow-lg'
                      : 'text-gray-600 hover:bg-white/50'
                  }`}
                >
                  <span className="mr-3">{item.icon}</span>
                  {item.label}
                </button>
              ))}
            </nav>

            {/* Stats in sidebar */}
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

        {/* Main content */}
        <main className="flex-1 p-6">
          <div className="mb-6 grid grid-cols-2 gap-4">
            <button className="glass-card group relative overflow-hidden p-8 text-left transition-all hover:scale-[1.02]">
              <div className="absolute inset-0 bg-linear-to-r from-blue-500 to-cyan-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
              <div className="relative">
                <div className="mb-2 text-4xl">⚡</div>
                <h3 className="text-xl font-bold text-gray-800 transition-colors group-hover:text-white">
                  Sync Everything
                </h3>
                <p className="mt-2 text-sm text-gray-600 transition-colors group-hover:text-white/90">
                  Push changes now
                </p>
              </div>
            </button>
            <button className="glass-card group relative overflow-hidden p-8 text-left transition-all hover:scale-[1.02]">
              <div className="absolute inset-0 bg-linear-to-r from-purple-500 to-pink-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
              <div className="relative">
                <div className="mb-2 text-4xl">📊</div>
                <h3 className="text-xl font-bold text-gray-800 transition-colors group-hover:text-white">
                  View Analytics
                </h3>
                <p className="mt-2 text-sm text-gray-600 transition-colors group-hover:text-white/90">
                  Check metrics
                </p>
              </div>
            </button>
          </div>

          {/* Platform grid */}
          <div className="grid gap-4 lg:grid-cols-2">
            {platforms.map((platform, i) => (
              <div
                key={platform.id}
                className="glass-card group relative overflow-hidden p-6 transition-all duration-500 hover:scale-[1.02]"
                style={{ animation: `floatIn 0.6s ease-out ${i * 0.1}s both` }}
              >
                <div className={`absolute inset-0 bg-linear-to-br ${platform.gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`} />
                
                <div className="relative">
                  <div className="mb-4 flex items-center justify-between">
                    <div className={`flex h-14 w-14 items-center justify-center rounded-2xl bg-linear-to-br ${platform.gradient} text-2xl text-white shadow-lg`}>
                      {platform.icon}
                    </div>
                    {platform.status === 'connected' && (
                      <div className="rounded-full bg-green-100 px-3 py-1 text-xs font-semibold text-green-800">
                        ● Connected
                      </div>
                    )}
                  </div>
                  
                  <h3 className="text-xl font-bold text-gray-800">{platform.name}</h3>
                  <p className="mt-1 text-sm text-gray-500">
                    {platform.status === 'connected' ? `${platform.tracks.toLocaleString()} tracks` : 'Not connected'}
                  </p>

                  <button
                    className={`mt-4 w-full rounded-xl py-3 text-sm font-semibold transition-all ${
                      platform.status === 'connected'
                        ? 'bg-gray-800 text-white hover:bg-gray-700'
                        : 'bg-linear-to-r from-indigo-500 to-purple-500 text-white hover:from-indigo-600 hover:to-purple-600'
                    }`}
                  >
                    {platform.status === 'connected' ? 'Manage' : 'Connect'}
                  </button>
                </div>
              </div>
            ))}
          </div>
        </main>
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

        @keyframes floatIn {
          from {
            opacity: 0;
            transform: translateY(30px);
          }
          to {
            opacity: 1;
            transform: translateY(0);
          }
        }
      `}</style>
    </div>
  );
}
