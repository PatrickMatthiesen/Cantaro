import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { DesignNav } from '../components/DesignNav';

// Design 2: "Liquid Glass Morphism"
// Aesthetic: Soft, ethereal, floating glass cards with flowing gradients and gentle animations
// Inspired by iOS design language with depth, blur, and luminous accents

export function Design2() {
  const { user } = useAuth();
  const [platforms] = useState([
    { id: 'youtube', name: 'YouTube Music', status: 'connected', tracks: 1247, icon: '▶', gradient: 'from-red-500 to-pink-500' },
    { id: 'spotify', name: 'Spotify', status: 'available', tracks: 0, icon: '♫', gradient: 'from-green-400 to-emerald-600' },
    { id: 'apple', name: 'Apple Music', status: 'available', tracks: 0, icon: '◉', gradient: 'from-pink-400 to-rose-500' },
    { id: 'tidal', name: 'Tidal', status: 'available', tracks: 0, icon: '◈', gradient: 'from-gray-700 to-gray-900' },
  ]);

  return (
    <div className="relative min-h-screen overflow-hidden bg-gradient-to-br from-indigo-50 via-purple-50 to-pink-50">
      <DesignNav currentDesign="2" style="light" />
      {/* Animated background orbs */}
      <div className="absolute -left-20 -top-20 h-80 w-80 animate-pulse rounded-full bg-gradient-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" style={{ animationDuration: '8s' }} />
      <div className="absolute -bottom-40 -right-20 h-96 w-96 animate-pulse rounded-full bg-gradient-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" style={{ animationDuration: '10s', animationDelay: '2s' }} />
      <div className="absolute left-1/2 top-1/2 h-72 w-72 -translate-x-1/2 -translate-y-1/2 animate-pulse rounded-full bg-gradient-to-br from-violet-300 to-fuchsia-300 opacity-20 blur-3xl" style={{ animationDuration: '12s', animationDelay: '4s' }} />

      <div className="relative z-10 mx-auto max-w-7xl p-6">
        {/* Header */}
        <header className="mb-12 mt-8">
          <div className="glass-card p-8">
            <div className="flex items-center justify-between">
              <div>
                <h1 className="bg-gradient-to-r from-indigo-600 via-purple-600 to-pink-600 bg-clip-text text-5xl font-bold tracking-tight text-transparent">
                  Cantaro
                </h1>
                <p className="mt-2 text-sm font-medium text-gray-600">{user?.email || 'Welcome back'}</p>
              </div>
              <div className="glass-card bg-gradient-to-br from-blue-50 to-purple-50 px-6 py-4">
                <div className="text-xs font-semibold uppercase tracking-wider text-gray-500">Library</div>
                <div className="mt-1 text-3xl font-bold text-gray-800">1,247</div>
                <div className="text-xs text-gray-500">tracks synced</div>
              </div>
            </div>
          </div>
        </header>

        {/* Quick stats */}
        <section className="mb-8 grid grid-cols-3 gap-4">
          {[
            { label: 'Active Syncs', value: '1', color: 'from-blue-400 to-cyan-400' },
            { label: 'Platforms Ready', value: '3', color: 'from-purple-400 to-pink-400' },
            { label: 'Sync Health', value: '98%', color: 'from-green-400 to-emerald-400' },
          ].map((stat, i) => (
            <div
              key={stat.label}
              className="glass-card p-6 text-center"
              style={{ animation: `floatIn 0.6s ease-out ${i * 0.15}s both` }}
            >
              <div className={`mx-auto mb-3 h-12 w-12 rounded-2xl bg-gradient-to-br ${stat.color}`} />
              <div className="text-2xl font-bold text-gray-800">{stat.value}</div>
              <div className="text-xs font-medium text-gray-500">{stat.label}</div>
            </div>
          ))}
        </section>

        {/* Platform cards */}
        <section className="mb-8">
          <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-gray-600">
            Your Platforms
          </h2>
          <div className="grid gap-4 md:grid-cols-2">
            {platforms.map((platform, i) => (
              <div
                key={platform.id}
                className="glass-card group relative overflow-hidden p-6 transition-all duration-500 hover:scale-[1.02]"
                style={{ animation: `floatIn 0.6s ease-out ${0.5 + i * 0.1}s both` }}
              >
                {/* Gradient background on hover */}
                <div className={`absolute inset-0 bg-gradient-to-br ${platform.gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`} />
                
                <div className="relative flex items-center justify-between">
                  <div className="flex items-center gap-4">
                    <div className={`flex h-16 w-16 items-center justify-center rounded-3xl bg-gradient-to-br ${platform.gradient} text-3xl text-white shadow-lg`}>
                      {platform.icon}
                    </div>
                    <div>
                      <h3 className="text-xl font-bold text-gray-800">{platform.name}</h3>
                      <p className="text-sm text-gray-500">
                        {platform.status === 'connected' ? `${platform.tracks.toLocaleString()} tracks` : 'Not connected'}
                      </p>
                    </div>
                  </div>
                  <button
                    className={`rounded-2xl px-6 py-3 text-sm font-semibold transition-all ${
                      platform.status === 'connected'
                        ? 'bg-gray-800 text-white hover:bg-gray-700'
                        : 'bg-gradient-to-r from-indigo-500 to-purple-500 text-white hover:from-indigo-600 hover:to-purple-600'
                    }`}
                  >
                    {platform.status === 'connected' ? 'Manage' : 'Connect'}
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>

        {/* Action buttons */}
        <section className="grid gap-4 md:grid-cols-2">
          <button className="glass-card group relative overflow-hidden p-8 text-left transition-all hover:scale-[1.02]">
            <div className="absolute inset-0 bg-gradient-to-r from-blue-500 to-cyan-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
            <div className="relative">
              <div className="mb-2 text-4xl">⚡</div>
              <h3 className="text-2xl font-bold text-gray-800 transition-colors group-hover:text-white">
                Sync Everything
              </h3>
              <p className="mt-2 text-sm text-gray-600 transition-colors group-hover:text-white/90">
                Push changes across all connected platforms
              </p>
            </div>
          </button>
          <button className="glass-card group relative overflow-hidden p-8 text-left transition-all hover:scale-[1.02]">
            <div className="absolute inset-0 bg-gradient-to-r from-purple-500 to-pink-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
            <div className="relative">
              <div className="mb-2 text-4xl">📊</div>
              <h3 className="text-2xl font-bold text-gray-800 transition-colors group-hover:text-white">
                View Analytics
              </h3>
              <p className="mt-2 text-sm text-gray-600 transition-colors group-hover:text-white/90">
                Check sync history and performance metrics
              </p>
            </div>
          </button>
        </section>
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
