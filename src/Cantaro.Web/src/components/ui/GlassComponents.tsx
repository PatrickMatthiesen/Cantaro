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
          className={`pointer-events-none absolute inset-0 bg-linear-to-br ${hoverGradient} opacity-0 transition-opacity duration-500 group-hover:opacity-10`}
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
        ? 'bg-white/70 text-gray-800 hover:bg-mist-100'
        : `bg-linear-to-r ${gradient} text-white hover:brightness-105`;

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
    <span className={`rounded-full px-3 py-1 text-xs font-semibold tracking-wide uppercase ${map[status]}`}>
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
          <p className="text-xs tracking-[0.22em] text-gray-500 uppercase">{label}</p>
          <p className="mt-2 text-3xl font-bold text-gray-800">{value}</p>
        </div>
        <div className={`flex h-11 w-11 items-center justify-center rounded-xl bg-linear-to-br ${gradient} text-lg text-white`}>
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
        <div className={`h-full bg-linear-to-r ${gradient}`} style={{ width: `${value}%` }} />
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
  selected?: boolean;
  onClick?: () => void;
}

export function PlatformTile({ platform, selected = false, onClick }: PlatformTileProps) {
  return (
    <button type="button" onClick={onClick} className="w-full text-left">
      <GlassCard
        interactive
        hoverGradient={platform.gradient}
        className={`group p-4 transition ${selected ? 'ring-2 ring-indigo-400/70' : ''}`}
      >
        <div className="flex items-start gap-3">
          <div className={`flex aspect-3/2 h-12 w-16 items-center justify-center rounded-xl bg-linear-to-br ${platform.gradient} text-white`}>
            {platform.icon}
          </div>
          <div className="w-full">
            <div>
              <p className="font-semibold text-gray-800">{platform.name}</p>
              <div className="mt-1 flex items-center justify-between gap-2">
                <p className="text-xs text-gray-500">{platform.tracks.toLocaleString()} tracks</p>
                <StatusBadge status={platform.status} />
              </div>
            </div>
          </div>
        </div>
      </GlassCard>
    </button>
  );
}

interface ActionFeatureCardProps {
  title: string;
  description: string;
  icon?: string;
  gradient?: string;
  onClick?: () => void;
}

export function ActionFeatureCard({
  title,
  description,
  icon = '⚡',
  gradient = 'from-indigo-500 to-purple-500',
  onClick,
}: ActionFeatureCardProps) {
  return (
    <button
      onClick={onClick}
      className="group relative w-full overflow-hidden rounded-3xl border border-white/80 bg-white/70 p-6 text-left shadow-[0_8px_32px_rgba(0,0,0,0.06),inset_0_1px_0_rgba(255,255,255,0.9)] backdrop-blur-[20px] transition-transform hover:scale-[1.01]"
    >
      <span
        className={`pointer-events-none absolute inset-0 bg-linear-to-r ${gradient} opacity-0 transition-opacity duration-500 group-hover:opacity-100`}
        aria-hidden
      />
      <div className="relative">
        <div className="mb-2 text-3xl">{icon}</div>
        <h4 className="text-xl font-bold text-gray-800 transition-colors group-hover:text-white">{title}</h4>
        <p className="mt-2 text-sm text-gray-600 transition-colors group-hover:text-white/90">{description}</p>
      </div>
    </button>
  );
}
