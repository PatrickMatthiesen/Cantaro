import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LayoutShell, platformCatalog } from './LayoutShell';

// Design 7: "Kanban Studio"
// Layout: Sidebar + three operational lanes with sticky lane headers and different card weights.

export function Design7() {
  const { user } = useAuth();
  const platforms = useMemo(() => platformCatalog, []);

  return (
    <LayoutShell currentDesign="7" userEmail={user?.email} sidebarSubtitle="Kanban Studio">
      <div className="space-y-4">
        <article className="glass-card flex items-center justify-between p-6">
          <div>
            <p className="text-xs uppercase tracking-[0.3em] text-gray-500">Design 7</p>
            <h2 className="mt-2 text-4xl font-bold text-gray-800">Pipeline board</h2>
          </div>
          <div className="flex gap-3">
            <button className="rounded-xl bg-gray-900 px-4 py-2 text-sm font-semibold text-white">Create job</button>
            <button className="rounded-xl bg-white/70 px-4 py-2 text-sm font-semibold text-gray-700">Filter</button>
          </div>
        </article>

        <section className="grid gap-4 xl:grid-cols-3">
          {[
            {
              title: 'Collect',
              tint: 'from-blue-500 to-cyan-500',
              items: [
                { name: 'Import liked songs delta', size: 'lg', icon: '📥' },
                { name: 'Read YouTube playlist metadata', size: 'md', icon: '🧾' },
                { name: 'Queue Spotify bootstrap', size: 'sm', icon: '⏳' },
              ],
            },
            {
              title: 'Resolve',
              tint: 'from-purple-500 to-pink-500',
              items: [
                { name: 'MBID direct match pass', size: 'md', icon: '🧠' },
                { name: 'Heuristic fallback comparison', size: 'lg', icon: '🧩' },
                { name: 'Manual ambiguity review', size: 'sm', icon: '👀' },
              ],
            },
            {
              title: 'Propagate',
              tint: 'from-emerald-500 to-lime-500',
              items: [
                { name: 'Write ordered tracks to service', size: 'lg', icon: '🚀' },
                { name: 'Verify playlist parity', size: 'md', icon: '✅' },
                { name: 'Store run telemetry', size: 'sm', icon: '📊' },
              ],
            },
          ].map((lane) => (
            <article key={lane.title} className="glass-card p-4">
              <div className="sticky top-0 z-10 mb-4 rounded-2xl bg-white/90 px-4 py-3 backdrop-blur">
                <div className={`mb-2 h-2 rounded-full bg-gradient-to-r ${lane.tint}`} />
                <h3 className="text-lg font-bold text-gray-800">{lane.title}</h3>
              </div>
              <div className="space-y-3">
                {lane.items.map((item) => (
                  <div
                    key={item.name}
                    className={`rounded-2xl bg-white/75 p-4 ${
                      item.size === 'lg' ? 'min-h-40' : item.size === 'md' ? 'min-h-28' : 'min-h-20'
                    }`}
                  >
                    <div className="flex items-start justify-between">
                      <p className="max-w-[85%] text-sm font-semibold text-gray-800">{item.name}</p>
                      <span>{item.icon}</span>
                    </div>
                    <div className="mt-3 flex items-center gap-2 text-xs text-gray-500">
                      <span className="rounded-full bg-gray-100 px-2 py-1">track-id</span>
                      <span className="rounded-full bg-gray-100 px-2 py-1">adapter</span>
                    </div>
                  </div>
                ))}
              </div>
            </article>
          ))}
        </section>

        <section className="grid gap-4 lg:grid-cols-4">
          {platforms.map((platform) => (
            <article key={platform.id} className="glass-card group relative overflow-hidden p-4">
              <div className={`absolute inset-0 bg-gradient-to-r ${platform.gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`} />
              <div className="relative flex items-center gap-3">
                <div className={`flex h-11 w-11 items-center justify-center rounded-xl bg-gradient-to-br ${platform.gradient} text-white`}>
                  {platform.icon}
                </div>
                <div>
                  <p className="font-semibold text-gray-800">{platform.name}</p>
                  <p className="text-xs text-gray-500">{platform.status}</p>
                </div>
              </div>
            </article>
          ))}
        </section>
      </div>
    </LayoutShell>
  );
}
