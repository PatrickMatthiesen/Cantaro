import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { DesignNav } from '../components/DesignNav';

// Design 5: "Masonry Feed" with Design 2 aesthetics
// Layout: Pinterest-style masonry grid with varied card heights

export function Design5() {
  const { user } = useAuth();
  const [platforms] = useState([
    { id: 'youtube', name: 'YouTube Music', status: 'connected', tracks: 1247, icon: '▶', gradient: 'from-red-500 to-pink-500' },
    { id: 'spotify', name: 'Spotify', status: 'available', tracks: 0, icon: '♫', gradient: 'from-green-400 to-emerald-600' },
    { id: 'apple', name: 'Apple Music', status: 'available', tracks: 0, icon: '◉', gradient: 'from-pink-400 to-rose-500' },
    { id: 'tidal', name: 'Tidal', status: 'available', tracks: 0, icon: '◈', gradient: 'from-gray-700 to-gray-900' },
  ]);

  return (
    <div className="relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50">
      <DesignNav currentDesign="5" style="light" />
      {/* Animated background orbs */}
      <div className="absolute top-0 left-1/3 h-150 w-150 animate-pulse rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" style={{ animationDuration: '8s' }} />
      <div className="absolute right-1/3 bottom-0 h-150 w-150 animate-pulse rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" style={{ animationDuration: '10s', animationDelay: '2s' }} />

      <div className="relative z-10 mx-auto max-w-7xl p-6">
        {/* Compact header */}
        <header className="glass-card mt-8 mb-8 p-6">
          <div className="flex items-center justify-between">
            <div>
              <h1 className="bg-linear-to-r from-indigo-600 via-purple-600 to-pink-600 bg-clip-text text-4xl font-bold text-transparent">
                Cantaro
              </h1>
              <p className="mt-1 text-sm text-gray-600">{user?.email || 'Welcome back'}</p>
            </div>
            <button className="rounded-2xl bg-linear-to-r from-indigo-500 to-purple-500 px-6 py-3 text-sm font-semibold text-white transition-all hover:scale-105">
              + Add Platform
            </button>
          </div>
        </header>

        {/* Masonry grid layout */}
        <div className="grid gap-4 md:grid-cols-3">
          {/* Stats card - tall */}
          <div
            className="glass-card row-span-2 p-6"
            style={{ animation: 'zoomIn 0.6s ease-out 0s both' }}
          >
            <h2 className="mb-6 text-sm font-semibold tracking-wider text-gray-600 uppercase">
              Library Stats
            </h2>
            <div className="space-y-6">
              {[
                { icon: '📚', label: 'Total Tracks', value: '1,247', color: 'from-blue-400 to-cyan-400' },
                { icon: '🔗', label: 'Connected', value: '1/4', color: 'from-purple-400 to-pink-400' },
                { icon: '✓', label: 'Sync Health', value: '98%', color: 'from-green-400 to-emerald-400' },
                { icon: '⚡', label: 'Last Sync', value: '2m ago', color: 'from-orange-400 to-red-400' },
              ].map((stat) => (
                <div key={stat.label} className="flex items-center gap-4">
                  <div className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-linear-to-br ${stat.color} text-xl`}>
                    {stat.icon}
                  </div>
                  <div className="flex-1">
                    <div className="text-sm text-gray-600">{stat.label}</div>
                    <div className="text-2xl font-bold text-gray-800">{stat.value}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Quick action cards - short */}
          <button
            className="glass-card group relative overflow-hidden p-6 text-left transition-all hover:scale-[1.02]"
            style={{ animation: 'zoomIn 0.6s ease-out 0.1s both' }}
          >
            <div className="absolute inset-0 bg-linear-to-r from-blue-500 to-cyan-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
            <div className="relative">
              <div className="mb-3 text-3xl">⚡</div>
              <h3 className="mb-1 text-xl font-bold text-gray-800 transition-colors group-hover:text-white">
                Sync All
              </h3>
              <p className="text-sm text-gray-600 transition-colors group-hover:text-white/90">
                Push changes now
              </p>
            </div>
          </button>

          <button
            className="glass-card group relative overflow-hidden p-6 text-left transition-all hover:scale-[1.02]"
            style={{ animation: 'zoomIn 0.6s ease-out 0.15s both' }}
          >
            <div className="absolute inset-0 bg-linear-to-r from-purple-500 to-pink-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
            <div className="relative">
              <div className="mb-3 text-3xl">📊</div>
              <h3 className="mb-1 text-xl font-bold text-gray-800 transition-colors group-hover:text-white">
                Analytics
              </h3>
              <p className="text-sm text-gray-600 transition-colors group-hover:text-white/90">
                View insights
              </p>
            </div>
          </button>

          {/* Platform cards - varying heights */}
          {platforms.map((platform, i) => (
            <div
              key={platform.id}
              className={`glass-card group relative overflow-hidden ${
                i === 0 ? 'md:col-span-2' : ''
              } p-6 transition-all duration-500 hover:scale-[1.02]`}
              style={{ animation: `zoomIn 0.6s ease-out ${0.2 + i * 0.1}s both` }}
            >
              <div className={`absolute inset-0 bg-linear-to-br ${platform.gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`} />
              
              <div className="relative">
                <div className="mb-4 flex items-center justify-between">
                  <div className={`flex h-16 w-16 items-center justify-center rounded-2xl bg-linear-to-br ${platform.gradient} text-3xl text-white shadow-lg`}>
                    {platform.icon}
                  </div>
                  {platform.status === 'connected' && (
                    <div className="rounded-full bg-green-100 px-3 py-1 text-xs font-semibold text-green-800">
                      ● Active
                    </div>
                  )}
                </div>

                <h3 className="mb-2 text-xl font-bold text-gray-800">{platform.name}</h3>
                
                {platform.status === 'connected' ? (
                  <>
                    <div className="mb-4 flex items-baseline gap-2">
                      <span className="text-3xl font-bold text-gray-800">{platform.tracks.toLocaleString()}</span>
                      <span className="text-sm text-gray-600">tracks synced</span>
                    </div>
                    <div className="mb-3 h-2 overflow-hidden rounded-full bg-gray-200">
                      <div className={`h-full bg-linear-to-r ${platform.gradient}`} style={{ width: '98%' }} />
                    </div>
                  </>
                ) : (
                  <p className="mb-4 text-sm text-gray-600">Ready to connect and sync your library</p>
                )}

                <button
                  className={`w-full rounded-xl py-3 text-sm font-semibold transition-all hover:scale-105 ${
                    platform.status === 'connected'
                      ? 'bg-gray-800 text-white hover:bg-gray-700'
                      : `bg-linear-to-r ${platform.gradient} text-white shadow-lg`
                  }`}
                >
                  {platform.status === 'connected' ? 'Manage' : 'Connect Now'}
                </button>
              </div>
            </div>
          ))}

          {/* Activity feed card - medium height */}
          <div
            className="glass-card p-6 md:col-span-2"
            style={{ animation: 'zoomIn 0.6s ease-out 0.6s both' }}
          >
            <h3 className="mb-4 text-sm font-semibold tracking-wider text-gray-600 uppercase">
              Recent Activity
            </h3>
            <div className="space-y-3">
              {[
                { action: 'Synced 43 tracks', platform: 'YouTube Music', time: '2 min ago', icon: '↻' },
                { action: 'New playlist created', platform: 'Local', time: '1 hour ago', icon: '+' },
                { action: 'Resolved 2 conflicts', platform: 'System', time: '3 hours ago', icon: '✓' },
              ].map((activity, i) => (
                <div key={i} className="flex items-center gap-3 rounded-xl bg-white/50 p-3">
                  <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-linear-to-br from-indigo-500 to-purple-500 text-sm text-white">
                    {activity.icon}
                  </div>
                  <div className="flex-1">
                    <div className="text-sm font-semibold text-gray-800">{activity.action}</div>
                    <div className="text-xs text-gray-600">{activity.platform} • {activity.time}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
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

        @keyframes zoomIn {
          from {
            opacity: 0;
            transform: scale(0.9);
          }
          to {
            opacity: 1;
            transform: scale(1);
          }
        }
      `}</style>
    </div>
  );
}
