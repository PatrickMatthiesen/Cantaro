import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LayoutShell, platformCatalog } from './LayoutShell';

// Design 4: "Ribbon Control Deck"
// Layout: Sidebar + top command ribbon with stacked command and timeline modules.

export function Design4() {
  const { user } = useAuth();
  const platforms = useMemo(() => platformCatalog, []);

  return (
    <LayoutShell currentDesign="4" userEmail={user?.email} sidebarSubtitle="Ribbon Deck">
      <div className="space-y-4">
        <section className="glass-card grid gap-3 p-4 lg:grid-cols-4">
          {[
            { label: 'Pipeline throughput', value: '432/h', color: 'from-blue-500 to-cyan-500' },
            { label: 'Ambiguous matches', value: '5', color: 'from-amber-500 to-orange-500' },
            { label: 'Pending retries', value: '9', color: 'from-purple-500 to-pink-500' },
            { label: 'Resolved today', value: '87', color: 'from-emerald-500 to-lime-500' },
          ].map((stat) => (
            <article key={stat.label} className="rounded-2xl bg-white/75 p-4">
              <div className={`mb-3 h-2 rounded-full bg-gradient-to-r ${stat.color}`} />
              <p className="text-xs uppercase tracking-[0.25em] text-gray-500">{stat.label}</p>
              <p className="mt-2 text-3xl font-bold text-gray-800">{stat.value}</p>
            </article>
          ))}
        </section>

        <section className="grid gap-4 xl:grid-cols-12">
          <article className="glass-card xl:col-span-7 xl:row-span-2 p-7">
            <div className="mb-4 flex items-center justify-between">
              <h2 className="text-2xl font-bold text-gray-800">Sync storyline</h2>
              <span className="rounded-full bg-indigo-100 px-3 py-1 text-xs font-semibold text-indigo-700">Live</span>
            </div>
            <ol className="space-y-4">
              {[
                { step: 'Ingestion', detail: 'Fetched 58 playlist changes from YouTube.' },
                { step: 'Mapping', detail: 'Attached 53 tracks by MBID/ISRC, queued 5 heuristic checks.' },
                { step: 'Propagation', detail: 'Pushed updates to connected targets with rate-limit backoff.' },
                { step: 'Verification', detail: 'Validated final list parity and marked job completed.' },
              ].map((entry, index) => (
                <li key={entry.step} className="flex items-start gap-3 rounded-2xl bg-white/70 p-4">
                  <div className="flex h-8 w-8 items-center justify-center rounded-full bg-gradient-to-br from-indigo-500 to-purple-500 text-xs font-bold text-white">
                    {index + 1}
                  </div>
                  <div>
                    <p className="text-sm font-semibold uppercase tracking-[0.22em] text-gray-500">{entry.step}</p>
                    <p className="mt-1 text-gray-700">{entry.detail}</p>
                  </div>
                </li>
              ))}
            </ol>
          </article>

          <article className="glass-card xl:col-span-5 p-6">
            <h3 className="text-sm uppercase tracking-[0.28em] text-gray-500">Platform deck</h3>
            <div className="mt-4 space-y-3">
              {platforms.map((platform) => (
                <div key={platform.id} className="flex items-center gap-3 rounded-2xl bg-white/70 p-3">
                  <div className={`flex h-11 w-11 items-center justify-center rounded-xl bg-gradient-to-br ${platform.gradient} text-white`}>
                    {platform.icon}
                  </div>
                  <div className="flex-1">
                    <p className="font-semibold text-gray-800">{platform.name}</p>
                    <p className="text-xs text-gray-500">{platform.tracks.toLocaleString()} tracks</p>
                  </div>
                  <button className="rounded-lg bg-gray-900 px-3 py-2 text-xs font-semibold text-white">
                    {platform.status === 'connected' ? 'Manage' : 'Connect'}
                  </button>
                </div>
              ))}
            </div>
          </article>

          <article className="glass-card xl:col-span-3 p-6">
            <h3 className="text-sm uppercase tracking-[0.28em] text-gray-500">Action stack</h3>
            <div className="mt-4 space-y-3">
              {[
                { label: 'Run sync', icon: '⚡', gradient: 'from-blue-500 to-cyan-500' },
                { label: 'Review conflicts', icon: '🧩', gradient: 'from-purple-500 to-pink-500' },
                { label: 'Schedule job', icon: '🕒', gradient: 'from-orange-500 to-rose-500' },
              ].map((action) => (
                <button key={action.label} className="group relative w-full overflow-hidden rounded-2xl p-4 text-left transition-transform hover:scale-[1.02]">
                  <span className={`absolute inset-0 bg-gradient-to-r ${action.gradient} opacity-90`} />
                  <span className="relative flex items-center justify-between text-sm font-semibold text-white">
                    <span>{action.label}</span>
                    <span>{action.icon}</span>
                  </span>
                </button>
              ))}
            </div>
          </article>

          <article className="glass-card xl:col-span-9 p-6">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="text-sm uppercase tracking-[0.28em] text-gray-500">Queue lanes</h3>
              <span className="text-xs text-gray-500">Updated 2m ago</span>
            </div>
            <div className="grid gap-3 md:grid-cols-3">
              {[
                { title: 'Ready', items: ['Sync workout mix', 'Import release radar'], tint: 'from-emerald-500 to-lime-500' },
                { title: 'Running', items: ['Propagate liked songs', 'Resolve ambiguous pair'], tint: 'from-indigo-500 to-cyan-500' },
                { title: 'Waiting', items: ['Apple backoff window', 'Retry playlist reorder'], tint: 'from-amber-500 to-orange-500' },
              ].map((lane) => (
                <div key={lane.title} className="rounded-2xl bg-white/70 p-4">
                  <div className={`mb-3 h-2 rounded-full bg-gradient-to-r ${lane.tint}`} />
                  <p className="text-sm font-semibold text-gray-800">{lane.title}</p>
                  <ul className="mt-2 space-y-2 text-sm text-gray-600">
                    {lane.items.map((item) => (
                      <li key={item} className="rounded-lg bg-white/70 px-3 py-2">
                        {item}
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          </article>
        </section>
      </div>
    </LayoutShell>
  );
}
