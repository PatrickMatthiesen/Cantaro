import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LayoutShell } from './LayoutShell';
import { platformCatalog } from './constants';

// Design 9: "Magazine Rails"
// Layout: Sidebar + horizontal feature rail, asymmetric bento floor, and compact action footer.

export function Design9() {
  const { user } = useAuth();
  const platforms = useMemo(() => platformCatalog, []);

  return (
    <LayoutShell currentDesign="9" userEmail={user?.email} sidebarSubtitle="Magazine Rails">
      <div className="space-y-4">
        <section className="glass-card p-5">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-2xl font-bold text-gray-800">Feature rail</h2>
            <span className="text-xs uppercase tracking-[0.24em] text-gray-500">Swipe horizontally</span>
          </div>
          <div className="flex gap-4 overflow-x-auto pb-2">
            {[
              { title: 'Global sync wave', copy: 'Trigger all connected services with one orchestration run.', gradient: 'from-blue-500 to-cyan-500' },
              { title: 'Conflict workshop', copy: 'Inspect ambiguous tracks and finalize canonical mapping.', gradient: 'from-purple-500 to-pink-500' },
              { title: 'Adapter diagnostics', copy: 'Check quotas, latencies, and retry pressure in one panel.', gradient: 'from-emerald-500 to-lime-500' },
            ].map((feature) => (
              <article key={feature.title} className="group relative min-w-90 overflow-hidden rounded-3xl p-6">
                <div className={`absolute inset-0 bg-linear-to-br ${feature.gradient}`} />
                <div className="relative text-white">
                  <p className="text-xs uppercase tracking-[0.24em] text-white/80">Feature</p>
                  <h3 className="mt-2 text-3xl font-bold">{feature.title}</h3>
                  <p className="mt-2 text-sm text-white/90">{feature.copy}</p>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section className="grid gap-4 xl:grid-cols-12">
          <article className="glass-card xl:col-span-7 p-6">
            <h3 className="text-sm uppercase tracking-[0.25em] text-gray-500">Platform stories</h3>
            <div className="mt-4 grid gap-3 md:grid-cols-2">
              {platforms.map((platform, index) => (
                <div key={platform.id} className={`group relative overflow-hidden rounded-2xl bg-white/70 p-4 ${index === 0 ? 'md:col-span-2' : ''}`}>
                  <div className={`absolute inset-0 bg-linear-to-r ${platform.gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`} />
                  <div className="relative flex items-center justify-between">
                    <div className="flex items-center gap-3">
                      <div className={`flex h-12 w-12 items-center justify-center rounded-xl bg-linear-to-br ${platform.gradient} text-white`}>
                        {platform.icon}
                      </div>
                      <div>
                        <p className="font-semibold text-gray-800">{platform.name}</p>
                        <p className="text-xs uppercase text-gray-500">{platform.status}</p>
                      </div>
                    </div>
                    <p className="text-sm font-semibold text-gray-700">{platform.tracks.toLocaleString()}</p>
                  </div>
                </div>
              ))}
            </div>
          </article>

          <article className="glass-card xl:col-span-5 p-6">
            <h3 className="text-sm uppercase tracking-[0.25em] text-gray-500">Signal snapshot</h3>
            <div className="mt-4 grid gap-3">
              {[
                { label: 'Sync certainty', value: '98.1%', gradient: 'from-indigo-500 to-cyan-500' },
                { label: 'Open ambiguities', value: '5', gradient: 'from-amber-500 to-orange-500' },
                { label: 'Pending writes', value: '17', gradient: 'from-purple-500 to-pink-500' },
              ].map((signal) => (
                <div key={signal.label} className="rounded-2xl bg-white/70 p-4">
                  <div className="mb-2 flex items-center justify-between">
                    <p className="text-xs uppercase tracking-[0.2em] text-gray-500">{signal.label}</p>
                    <p className="text-xl font-bold text-gray-800">{signal.value}</p>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-gray-200">
                    <div className={`h-full bg-linear-to-r ${signal.gradient}`} style={{ width: signal.value === '5' ? '22%' : signal.value === '17' ? '64%' : '98%' }} />
                  </div>
                </div>
              ))}
            </div>
          </article>

          <article className="glass-card xl:col-span-8 p-6">
            <h3 className="text-sm uppercase tracking-[0.25em] text-gray-500">Activity strip</h3>
            <div className="mt-4 flex gap-3 overflow-x-auto pb-1">
              {[
                'Scheduled nightly sync for 04:00',
                'Normalized 12 title variants',
                'Merged duplicate source IDs',
                'Queued rollback for failed write batch',
                'Completed verification on 4 playlists',
              ].map((item) => (
                <div key={item} className="min-w-55 rounded-2xl bg-white/75 px-4 py-3 text-sm text-gray-700">
                  {item}
                </div>
              ))}
            </div>
          </article>

          <article className="glass-card xl:col-span-4 p-6">
            <h3 className="text-sm uppercase tracking-[0.25em] text-gray-500">Fast actions</h3>
            <div className="mt-4 space-y-3">
              {[
                { label: 'Sync now', gradient: 'from-blue-500 to-cyan-500' },
                { label: 'Open conflict queue', gradient: 'from-purple-500 to-pink-500' },
                { label: 'Export telemetry', gradient: 'from-emerald-500 to-lime-500' },
              ].map((action) => (
                <button key={action.label} className={`w-full rounded-xl bg-linear-to-r ${action.gradient} px-4 py-3 text-left text-sm font-semibold text-white`}>
                  {action.label}
                </button>
              ))}
            </div>
          </article>
        </section>
      </div>
    </LayoutShell>
  );
}
