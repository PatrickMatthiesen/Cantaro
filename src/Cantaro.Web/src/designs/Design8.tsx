import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LayoutShell } from './LayoutShell';
import { platformCatalog } from './constants';

// Design 8: "Orbit Studio"
// Layout: Sidebar + radial hub canvas with orbiting service nodes and right-side inspector.

export function Design8() {
  const { user } = useAuth();
  const platforms = useMemo(() => platformCatalog, []);

  return (
    <LayoutShell currentDesign="8" userEmail={user?.email} sidebarSubtitle="Orbit Studio">
      <div className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
        <section className="glass-card relative min-h-190 overflow-hidden p-6">
          <p className="text-xs tracking-[0.28em] text-gray-500 uppercase">Service topology</p>
          <h2 className="mt-2 text-3xl font-bold text-gray-800">Orbit map</h2>

          <div className="absolute inset-0 flex items-center justify-center">
            <div className="relative h-140 w-140 rounded-full border border-white/60 bg-white/30">
              <div className="absolute top-1/2 left-1/2 flex h-44 w-44 -translate-x-1/2 -translate-y-1/2 flex-col items-center justify-center rounded-full bg-linear-to-br from-indigo-500 to-purple-500 text-white shadow-xl">
                <span className="text-xs tracking-[0.2em] uppercase">Core</span>
                <span className="mt-2 text-3xl font-bold">TrackID</span>
              </div>

              {platforms.map((platform, index) => {
                const positions = [
                  'left-[50%] top-[6%] -translate-x-1/2',
                  'right-[6%] top-[50%] -translate-y-1/2',
                  'left-[50%] bottom-[6%] -translate-x-1/2',
                  'left-[6%] top-[50%] -translate-y-1/2',
                ];

                return (
                  <button
                    key={platform.id}
                    className={`absolute ${positions[index]} group w-44 rounded-2xl bg-white/80 p-3 text-left shadow-lg transition-transform hover:scale-105`}
                  >
                    <div className="flex items-center gap-2">
                      <div className={`flex h-9 w-9 items-center justify-center rounded-lg bg-linear-to-br ${platform.gradient} text-sm text-white`}>
                        {platform.icon}
                      </div>
                      <div>
                        <p className="text-sm font-semibold text-gray-800">{platform.name}</p>
                        <p className="text-xs text-gray-500 uppercase">{platform.status}</p>
                      </div>
                    </div>
                    <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-gray-200">
                      <div className={`h-full bg-linear-to-r ${platform.gradient}`} style={{ width: platform.status === 'connected' ? '96%' : '28%' }} />
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="absolute right-6 bottom-6 left-6 grid gap-3 md:grid-cols-3">
            {[
              { label: 'Cross-service consistency', value: '97.9%' },
              { label: 'Pending link confirmations', value: '11' },
              { label: 'Topology refresh', value: '45s' },
            ].map((metric) => (
              <div key={metric.label} className="rounded-2xl bg-white/80 p-4">
                <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">{metric.label}</p>
                <p className="mt-2 text-2xl font-bold text-gray-800">{metric.value}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="space-y-4">
          <article className="glass-card p-6">
            <h3 className="text-sm tracking-[0.28em] text-gray-500 uppercase">Inspector</h3>
            <div className="mt-4 rounded-2xl bg-white/70 p-4">
              <p className="text-xs tracking-[0.2em] text-gray-500 uppercase">Selected node</p>
              <p className="mt-2 text-2xl font-bold text-gray-800">YouTube Music</p>
              <p className="mt-1 text-sm text-gray-600">Connected · 1,247 tracks · last synced 2m ago</p>
            </div>
            <div className="mt-4 space-y-3 text-sm text-gray-700">
              <div className="rounded-xl bg-white/70 px-3 py-2">Mapping method: MBID primary</div>
              <div className="rounded-xl bg-white/70 px-3 py-2">Retry backoff: idle</div>
              <div className="rounded-xl bg-white/70 px-3 py-2">Conflict policy: explicit review</div>
            </div>
          </article>

          <button className="glass-card group relative w-full overflow-hidden p-6 text-left">
            <span className="absolute inset-0 bg-linear-to-r from-blue-500 to-cyan-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
            <span className="relative block">
              <p className="text-xs tracking-[0.24em] text-gray-500 uppercase group-hover:text-white/80">Action</p>
              <p className="mt-1 text-2xl font-bold text-gray-800 group-hover:text-white">Run topology sync</p>
              <p className="mt-1 text-sm text-gray-600 group-hover:text-white/90">Refresh service links and recalculate confidence.</p>
            </span>
          </button>

          <article className="glass-card p-6">
            <h3 className="text-sm tracking-[0.28em] text-gray-500 uppercase">Event feed</h3>
            <ul className="mt-4 space-y-2">
              {[
                'Connected account token refreshed',
                'Detected playlist rename drift',
                'Queued propagation to secondary targets',
                'Marked one result as no match',
              ].map((event) => (
                <li key={event} className="rounded-xl bg-white/70 px-3 py-2 text-sm text-gray-700">
                  {event}
                </li>
              ))}
            </ul>
          </article>
        </section>
      </div>
    </LayoutShell>
  );
}
