import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { DesignNav } from '../components/DesignNav';
import {
  ActionFeatureCard,
  GlassCard,
  GradientButton,
  PlatformTile,
  StatusBadge,
} from '../components/ui/GlassComponents';

export function ComponentsPage() {
  const { user } = useAuth();

  const samplePlatforms = useMemo(
    () => [
      { name: 'YouTube Music', status: 'connected' as const, tracks: 1247, icon: '▶', gradient: 'from-red-500 to-pink-500' },
      { name: 'Spotify', status: 'available' as const, tracks: 0, icon: '♫', gradient: 'from-green-400 to-emerald-600' },
      { name: 'Apple Music', status: 'warning' as const, tracks: 318, icon: '◉', gradient: 'from-pink-400 to-rose-500' },
    ],
    [],
  );

  return (
    <div className="relative min-h-screen overflow-hidden bg-gradient-to-br from-indigo-50 via-purple-50 to-pink-50 text-gray-900">
      <DesignNav currentDesign="components" style="light" />
      <div className="absolute -left-20 -top-20 h-80 w-80 rounded-full bg-gradient-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -bottom-40 -right-20 h-96 w-96 rounded-full bg-gradient-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />

      <div className="relative z-10 mx-auto max-w-6xl space-y-5 px-6 pb-28 pt-8">
        <GlassCard className="p-7">
          <p className="text-xs uppercase tracking-[0.3em] text-gray-500">Cantaro UI gallery</p>
          <h1 className="mt-2 bg-gradient-to-r from-indigo-600 to-pink-600 bg-clip-text text-4xl font-bold text-transparent">
            Core components
          </h1>
          <p className="mt-2 text-gray-600">
            Reusable building blocks with our color palette, glass surfaces, and hover interactions.
          </p>
          <p className="mt-1 text-sm text-gray-500">Signed in as {user?.email ?? 'Unknown user'}</p>
        </GlassCard>

        <section className="grid gap-4 xl:grid-cols-2">
          <GlassCard className="p-6">
            <h2 className="text-sm uppercase tracking-[0.24em] text-gray-500">Buttons</h2>
            <div className="mt-4 flex flex-wrap gap-3">
              <GradientButton>Primary action</GradientButton>
              <GradientButton gradient="from-blue-500 to-cyan-500">Info action</GradientButton>
              <GradientButton tone="dark">Dark action</GradientButton>
              <GradientButton tone="soft">Soft action</GradientButton>
            </div>
          </GlassCard>

          <GlassCard className="p-6">
            <h2 className="text-sm uppercase tracking-[0.24em] text-gray-500">Status badges</h2>
            <div className="mt-4 flex flex-wrap gap-3">
              <StatusBadge status="connected" />
              <StatusBadge status="available" />
              <StatusBadge status="warning" />
            </div>
          </GlassCard>
        </section>

        <GlassCard className="p-6">
          <h2 className="text-sm uppercase tracking-[0.24em] text-gray-500">Action cards</h2>
          <div className="mt-4 grid gap-4 md:grid-cols-2">
            <ActionFeatureCard
              icon="⚡"
              title="Sync everything"
              description="Run a full sync across all connected services."
              gradient="from-blue-500 to-cyan-500"
            />
            <ActionFeatureCard
              icon="📊"
              title="View analytics"
              description="Open sync history and operational insights."
              gradient="from-purple-500 to-pink-500"
            />
          </div>
        </GlassCard>

        <GlassCard className="p-6">
          <h2 className="text-sm uppercase tracking-[0.24em] text-gray-500">Platform tiles</h2>
          <div className="mt-4 grid gap-4 lg:grid-cols-3">
            {samplePlatforms.map((platform) => (
              <PlatformTile key={platform.name} platform={platform} />
            ))}
          </div>
        </GlassCard>
      </div>
    </div>
  );
}
