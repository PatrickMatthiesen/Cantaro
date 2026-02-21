import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LayoutShell } from './LayoutShell';
import { platformCatalog } from './constants';

// Design 6: "Panorama Command Deck"
// Layout: Sidebar + center spotlight panel, narrow left widgets, dense right telemetry strip.

export function Design6() {
  const { user } = useAuth();
  const platforms = useMemo(() => platformCatalog, []);

  return (
    <LayoutShell currentDesign="6" userEmail={user?.email} sidebarSubtitle="Panorama Deck">
      <div className="grid gap-4 xl:grid-cols-[0.75fr_1.35fr_0.9fr]">
        <section className="space-y-4">
          {[
            { label: 'Auto-match confidence', value: '94%', icon: '🎯', color: 'from-indigo-500 to-cyan-500' },
            { label: 'Sync cycles today', value: '28', icon: '🔁', color: 'from-purple-500 to-pink-500' },
            { label: 'Manual reviews', value: '6', icon: '🧭', color: 'from-amber-500 to-orange-500' },
          ].map((metric) => (
            <article key={metric.label} className="glass-card p-5">
              <div className="flex items-start justify-between">
                <div>
                  <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">{metric.label}</p>
                  <p className="mt-2 text-4xl font-bold text-gray-800">{metric.value}</p>
                </div>
                <div className={`flex h-12 w-12 items-center justify-center rounded-2xl bg-linear-to-br ${metric.color} text-xl text-white`}>
                  {metric.icon}
                </div>
              </div>
            </article>
          ))}
        </section>

        <section className="space-y-4">
          <article className="glass-card relative overflow-hidden p-8">
            <div className="absolute top-10 -right-20 h-56 w-56 rounded-full bg-linear-to-br from-indigo-400 to-fuchsia-400 opacity-30 blur-3xl" />
            <p className="text-xs tracking-[0.24em] text-gray-500 uppercase">Central command</p>
            <h2 className="mt-3 text-5xl font-bold text-gray-800">Sync director</h2>
            <p className="mt-3 max-w-xl text-gray-600">
              Prioritize jobs, route retries, and monitor adapter health from one high-focus command surface.
            </p>
            <div className="mt-7 grid gap-3 sm:grid-cols-2">
              <button className="group relative overflow-hidden rounded-2xl p-5 text-left">
                <span className="absolute inset-0 bg-linear-to-r from-blue-500 to-cyan-500 opacity-90" />
                <span className="relative text-white">
                  <p className="text-xs tracking-[0.2em] uppercase">Run now</p>
                  <p className="mt-1 text-xl font-bold">Execute full sync</p>
                </span>
              </button>
              <button className="group relative overflow-hidden rounded-2xl p-5 text-left">
                <span className="absolute inset-0 bg-linear-to-r from-purple-500 to-pink-500 opacity-90" />
                <span className="relative text-white">
                  <p className="text-xs tracking-[0.2em] uppercase">Plan</p>
                  <p className="mt-1 text-xl font-bold">Open scheduler</p>
                </span>
              </button>
            </div>
          </article>

          <div className="grid gap-4 md:grid-cols-2">
            {platforms.map((platform) => (
              <article key={platform.id} className="glass-card group relative overflow-hidden p-5 transition-transform hover:scale-[1.02]">
                <div className={`absolute inset-0 bg-linear-to-br ${platform.gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`} />
                <div className="relative">
                  <div className="flex items-center justify-between">
                    <div className={`flex h-12 w-12 items-center justify-center rounded-xl bg-linear-to-br ${platform.gradient} text-white`}>
                      {platform.icon}
                    </div>
                    <span className="text-xs font-semibold text-gray-500 uppercase">{platform.status}</span>
                  </div>
                  <h3 className="mt-3 text-xl font-bold text-gray-800">{platform.name}</h3>
                  <p className="text-sm text-gray-600">{platform.tracks.toLocaleString()} tracks</p>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="space-y-4">
          <article className="glass-card p-6">
            <h3 className="text-sm tracking-[0.26em] text-gray-500 uppercase">Adapter telemetry</h3>
            <div className="mt-4 space-y-3">
              {[
                { name: 'YouTube', rate: '12 req/min', health: 96 },
                { name: 'Spotify', rate: 'N/A', health: 0 },
                { name: 'Apple', rate: 'N/A', health: 0 },
                { name: 'Tidal', rate: 'N/A', health: 0 },
              ].map((row) => (
                <div key={row.name}>
                  <div className="mb-1 flex justify-between text-xs text-gray-600">
                    <span>{row.name}</span>
                    <span>{row.rate}</span>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-gray-200">
                    <div className="h-full bg-linear-to-r from-indigo-500 to-cyan-500" style={{ width: `${row.health}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </article>

          <article className="glass-card p-6">
            <h3 className="text-sm tracking-[0.26em] text-gray-500 uppercase">Recent decisions</h3>
            <ul className="mt-4 space-y-2 text-sm text-gray-700">
              {[
                'Fallback to heuristic title match for 2 tracks',
                'Deferred playlist reorder due to quota',
                'Marked one entry as ambiguous for review',
              ].map((decision) => (
                <li key={decision} className="rounded-xl bg-white/70 px-3 py-2">
                  {decision}
                </li>
              ))}
            </ul>
          </article>
        </section>
      </div>
    </LayoutShell>
  );
}
