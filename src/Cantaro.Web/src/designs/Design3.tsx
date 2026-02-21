import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LayoutShell } from './LayoutShell';
import { platformCatalog } from './constants';


// Design 3: "Pulse Mosaic"
// Layout: Sidebar + oversized hero, staggered platform tiles, and a right rail command stack.

export function Design3() {
  const { user } = useAuth();
  const platforms = useMemo(
    () =>
      platformCatalog.map((platform) => ({
        ...platform,
        desc:
          platform.status === 'connected'
            ? 'Primary sync source with active propagation.'
            : 'Ready to connect with conflict-safe import.',
      })),
    [],
  );

  return (
    <LayoutShell currentDesign="3" userEmail={user?.email} sidebarSubtitle="Pulse Mosaic">
      <div className="grid gap-4 xl:grid-cols-[1.35fr_0.65fr]">
        <section className="space-y-4">
          <article className="glass-card relative overflow-hidden p-8">
            <div className="absolute -top-16 -right-16 h-52 w-52 rounded-full bg-linear-to-br from-indigo-300 to-fuchsia-300 opacity-40 blur-3xl" />
            <p className="text-xs tracking-[0.3em] text-gray-500 uppercase">Design 3 · Pulse Mosaic</p>
            <h2 className="mt-3 bg-linear-to-r from-indigo-600 to-pink-600 bg-clip-text text-5xl font-bold text-transparent">
              Build your sync canvas
            </h2>
            <p className="mt-3 max-w-2xl text-gray-600">
              A layout with oversized hero context, mixed-card rhythm, and quick control surfaces for rapid music operations.
            </p>
            <div className="mt-6 grid gap-3 sm:grid-cols-3">
              {[
                { label: 'Queued jobs', value: '14' },
                { label: 'Retries today', value: '3' },
                { label: 'Conflict rate', value: '0.4%' },
              ].map((stat) => (
                <div key={stat.label} className="rounded-2xl bg-white/70 p-4">
                  <p className="text-xs tracking-wide text-gray-500 uppercase">{stat.label}</p>
                  <p className="mt-1 text-2xl font-bold text-gray-800">{stat.value}</p>
                </div>
              ))}
            </div>
          </article>

          <div className="grid gap-4 md:grid-cols-12">
            {platforms.map((platform, index) => (
              <article
                key={platform.id}
                className={`glass-card group relative overflow-hidden p-5 transition-transform hover:scale-[1.01] ${
                  index === 0 ? 'md:col-span-8 md:row-span-2' : 'md:col-span-4'
                }`}
              >
                <div className={`absolute inset-0 bg-linear-to-br ${platform.gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`} />
                <div className="relative flex h-full flex-col justify-between gap-4">
                  <div className="flex items-start justify-between">
                    <div className={`flex h-14 w-14 items-center justify-center rounded-2xl bg-linear-to-br ${platform.gradient} text-2xl text-white`}>
                      {platform.icon}
                    </div>
                    <span className="rounded-full bg-white/80 px-3 py-1 text-xs font-semibold text-gray-600 uppercase">
                      {platform.status}
                    </span>
                  </div>
                  <div>
                    <h3 className="text-2xl font-bold text-gray-800">{platform.name}</h3>
                    <p className="mt-1 text-sm text-gray-600">{platform.desc}</p>
                    <p className="mt-3 text-xl font-semibold text-gray-800">
                      {platform.tracks.toLocaleString()} <span className="text-sm font-normal text-gray-500">tracks</span>
                    </p>
                  </div>
                  <button className="w-full rounded-xl bg-gray-900 py-2 text-sm font-semibold text-white transition-colors hover:bg-gray-700">
                    {platform.status === 'connected' ? `Manage ${platform.name}` : 'Connect Account'}
                  </button>
                </div>
              </article>
            ))}
          </div>
        </section>

        <aside className="space-y-4">
          <button className="glass-card group relative w-full overflow-hidden p-6 text-left transition-transform hover:scale-[1.02]">
            <div className="absolute inset-0 bg-linear-to-r from-blue-500 to-cyan-500 opacity-0 transition-opacity duration-500 group-hover:opacity-100" />
            <div className="relative">
              <p className="text-xs tracking-[0.25em] text-gray-500 uppercase group-hover:text-white/80">Primary Action</p>
              <h3 className="mt-2 text-2xl font-bold text-gray-800 group-hover:text-white">Sync all now</h3>
              <p className="mt-1 text-sm text-gray-600 group-hover:text-white/90">Push pending changes across connected platforms.</p>
            </div>
          </button>

          <div className="glass-card p-6">
            <p className="text-xs tracking-[0.25em] text-gray-500 uppercase">Live timeline</p>
            <ul className="mt-4 space-y-3">
              {[
                'Resolved Spotify duplicate mapping',
                'Applied YouTube rename to 3 playlists',
                'Backoff window ended for Apple sync',
              ].map((event) => (
                <li key={event} className="rounded-xl bg-white/70 px-3 py-2 text-sm text-gray-700">
                  {event}
                </li>
              ))}
            </ul>
          </div>

          <div className="glass-card p-6">
            <p className="text-xs tracking-[0.25em] text-gray-500 uppercase">Health bands</p>
            <div className="mt-4 space-y-3">
              {[
                { label: 'Adapter latency', value: 72, color: 'from-blue-500 to-cyan-500' },
                { label: 'Mapping confidence', value: 94, color: 'from-purple-500 to-pink-500' },
                { label: 'Queue pressure', value: 35, color: 'from-emerald-500 to-lime-500' },
              ].map((bar) => (
                <div key={bar.label}>
                  <div className="mb-1 flex justify-between text-xs text-gray-600">
                    <span>{bar.label}</span>
                    <span>{bar.value}%</span>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-gray-200">
                    <div className={`h-full bg-linear-to-r ${bar.color}`} style={{ width: `${bar.value}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </div>
        </aside>
      </div>
    </LayoutShell>
  );
}
