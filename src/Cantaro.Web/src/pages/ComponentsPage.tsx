import { useMemo } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { LayoutShell } from '../designs/LayoutShell';
import {
  GlassCard,
  GradientButton,
  MetricTile,
  PlatformTile,
  ProgressMeter,
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
    <LayoutShell currentDesign="components" userEmail={user?.email} sidebarSubtitle="Component Library">
      <div className="space-y-5">
        <GlassCard className="p-7">
          <p className="text-xs uppercase tracking-[0.3em] text-gray-500">UI Kit</p>
          <h2 className="mt-2 bg-gradient-to-r from-indigo-600 to-pink-600 bg-clip-text text-4xl font-bold text-transparent">
            Core components
          </h2>
          <p className="mt-2 text-gray-600">
            Shared building blocks using Cantaro colors, glass surfaces, and hover interactions.
          </p>
        </GlassCard>

        <section className="grid gap-4 xl:grid-cols-2">
          <GlassCard className="p-6">
            <h3 className="text-sm uppercase tracking-[0.24em] text-gray-500">Buttons</h3>
            <div className="mt-4 flex flex-wrap gap-3">
              <GradientButton>Primary action</GradientButton>
              <GradientButton gradient="from-blue-500 to-cyan-500">Info action</GradientButton>
              <GradientButton tone="dark">Dark action</GradientButton>
              <GradientButton tone="soft">Soft action</GradientButton>
            </div>
          </GlassCard>

          <GlassCard className="p-6">
            <h3 className="text-sm uppercase tracking-[0.24em] text-gray-500">Status badges</h3>
            <div className="mt-4 flex flex-wrap gap-3">
              <StatusBadge status="connected" />
              <StatusBadge status="available" />
              <StatusBadge status="warning" />
            </div>
          </GlassCard>
        </section>

        <section className="grid gap-4 lg:grid-cols-4">
          <MetricTile label="Total tracks" value="1,247" icon="🎵" />
          <MetricTile label="Sync health" value="98%" icon="⚡" gradient="from-emerald-500 to-lime-500" />
          <MetricTile label="Retry queue" value="9" icon="↻" gradient="from-amber-500 to-orange-500" />
          <MetricTile label="Ambiguous" value="5" icon="🧩" gradient="from-purple-500 to-pink-500" />
        </section>

        <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
          <GlassCard className="p-6">
            <h3 className="text-sm uppercase tracking-[0.24em] text-gray-500">Progress meters</h3>
            <div className="mt-4 space-y-4">
              <ProgressMeter label="Mapping confidence" value={94} />
              <ProgressMeter label="Sync completion" value={81} gradient="from-blue-500 to-cyan-500" />
              <ProgressMeter label="Queue pressure" value={37} gradient="from-amber-500 to-orange-500" />
            </div>
          </GlassCard>

          <GlassCard className="p-6">
            <h3 className="text-sm uppercase tracking-[0.24em] text-gray-500">Interactive card sample</h3>
            <GlassCard interactive hoverGradient="from-indigo-500 to-purple-500" className="group mt-4 p-5">
              <p className="text-sm text-gray-600">Hover this card to preview the gradient overlay behavior.</p>
              <p className="mt-2 text-lg font-semibold text-gray-800">Reusable hover surface</p>
            </GlassCard>
          </GlassCard>
        </section>

        <section className="grid gap-4 lg:grid-cols-3">
          {samplePlatforms.map((platform) => (
            <PlatformTile key={platform.name} platform={platform} />
          ))}
        </section>
      </div>
    </LayoutShell>
  );
}
