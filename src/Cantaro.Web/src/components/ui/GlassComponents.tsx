import type { ButtonHTMLAttributes, HTMLAttributes } from 'react';

export const glassSurfaceClass =
  'rounded-3xl border border-white/80 bg-white/70 shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px]';

interface GlassCardProps extends HTMLAttributes<HTMLDivElement> {
  interactive?: boolean;
  hoverGradient?: string;
}

export function GlassCard({
  interactive = false,
  hoverGradient,
  className = '',
  children,
  ...rest
}: GlassCardProps) {
  return (
    <div
      className={`relative overflow-hidden ${glassSurfaceClass} ${
        interactive ? 'transition-transform duration-300 hover:scale-[1.01]' : ''
      } ${className}`}
      {...rest}
    >
      {hoverGradient ? (
        <span
          className={`pointer-events-none absolute inset-0 bg-gradient-to-br ${hoverGradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`}
          aria-hidden
        />
      ) : null}
      <div className="relative">{children}</div>
    </div>
  );
}

interface GradientButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  gradient?: string;
  tone?: 'primary' | 'dark' | 'soft';
}

export function GradientButton({
  gradient = 'from-indigo-500 to-purple-500',
  tone = 'primary',
  className = '',
  children,
  ...rest
}: GradientButtonProps) {
  const toneClass =
    tone === 'dark'
      ? 'bg-gray-900 text-white hover:bg-gray-700'
      : tone === 'soft'
        ? 'bg-white/70 text-gray-800 hover:bg-white'
        : `bg-gradient-to-r ${gradient} text-white hover:brightness-105`;

  return (
    <button
      className={`rounded-2xl px-5 py-3 text-sm font-semibold transition-all hover:scale-[1.03] ${toneClass} ${className}`}
      {...rest}
    >
      {children}
    </button>
  );
}

interface StatusBadgeProps {
  status: 'connected' | 'available' | 'warning';
}

export function StatusBadge({ status }: StatusBadgeProps) {
  const map = {
    connected: 'bg-green-100 text-green-800',
    available: 'bg-slate-100 text-slate-700',
    warning: 'bg-amber-100 text-amber-800',
  } as const;

  return (
    <span className={`rounded-full px-3 py-1 text-xs font-semibold uppercase tracking-wide ${map[status]}`}>
      {status}
    </span>
  );
}

interface MetricTileProps {
  label: string;
  value: string;
  icon?: string;
  gradient?: string;
}

export function MetricTile({
  label,
  value,
  icon = '✦',
  gradient = 'from-indigo-500 to-cyan-500',
}: MetricTileProps) {
  return (
    <GlassCard className="p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-xs uppercase tracking-[0.22em] text-gray-500">{label}</p>
          <p className="mt-2 text-3xl font-bold text-gray-800">{value}</p>
        </div>
        <div className={`flex h-11 w-11 items-center justify-center rounded-xl bg-gradient-to-br ${gradient} text-lg text-white`}>
          {icon}
        </div>
      </div>
    </GlassCard>
  );
}

interface ProgressMeterProps {
  label: string;
  value: number;
  gradient?: string;
}

export function ProgressMeter({
  label,
  value,
  gradient = 'from-indigo-500 to-purple-500',
}: ProgressMeterProps) {
  return (
    <div>
      <div className="mb-1 flex justify-between text-xs text-gray-600">
        <span>{label}</span>
        <span>{value}%</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-gray-200">
        <div className={`h-full bg-gradient-to-r ${gradient}`} style={{ width: `${value}%` }} />
      </div>
    </div>
  );
}

interface PlatformTileProps {
  platform: {
    name: string;
    status: 'connected' | 'available' | 'warning';
    tracks: number;
    icon: string;
    gradient: string;
  };
}

export function PlatformTile({ platform }: PlatformTileProps) {
  return (
    <GlassCard interactive hoverGradient={platform.gradient} className="group p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className={`flex h-12 w-12 items-center justify-center rounded-xl bg-gradient-to-br ${platform.gradient} text-white`}>
            {platform.icon}
          </div>
          <div>
            <p className="font-semibold text-gray-800">{platform.name}</p>
            <p className="text-xs text-gray-500">{platform.tracks.toLocaleString()} tracks</p>
          </div>
        </div>
        <StatusBadge status={platform.status} />
      </div>
    </GlassCard>
  );
}
