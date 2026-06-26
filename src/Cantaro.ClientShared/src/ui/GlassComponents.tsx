import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from 'react';

const glassSurfaceClass =
  'glass-surface rounded-[1.5rem] border border-white/80 bg-white/70 shadow-[0_12px_34px_rgba(88,74,150,0.06)] backdrop-blur';

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
      className={`relative overflow-hidden ${glassSurfaceClass} ${interactive ? 'transition-transform duration-300 hover:scale-[1.01]' : ''
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
  gradient,
  tone = 'primary',
  className = '',
  children,
  ...rest
}: GradientButtonProps) {
  const toneClass =
    tone === 'dark'
      ? 'bg-slate-950 text-white hover:bg-violet-700'
      : tone === 'soft'
        ? 'border border-[#e3def8] bg-white/70 text-slate-800 hover:bg-violet-50 hover:text-violet-800'
        : gradient
          ? `bg-linear-to-r ${gradient} text-white hover:brightness-105`
          : 'bg-slate-950 text-white hover:bg-violet-700';

  return (
    <button
      className={`rounded-2xl px-5 py-3 text-sm font-black transition-colors ${toneClass} disabled:cursor-not-allowed disabled:bg-none disabled:bg-slate-200 disabled:text-slate-500 disabled:shadow-none disabled:hover:bg-slate-200 disabled:hover:brightness-100 ${className}`}
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
    connected: 'status-badge--connected',
    available: 'status-badge--available',
    warning: 'status-badge--warning',
  } as const;

  return (
    <span className={`rounded-full px-3 py-1 text-xs font-semibold tracking-wide uppercase ${map[status]}`}>
      {status}
    </span>
  );
}

interface PillProps extends HTMLAttributes<HTMLSpanElement> {
  tone?: 'neutral' | 'info' | 'success' | 'warning';
  size?: 'compact' | 'regular';
}

export function Pill({ tone = 'neutral', size = 'regular', className = '', children, ...rest }: PillProps) {
  return (
    <span
      className={`ui-pill ui-pill--${tone} inline-flex items-center rounded-full font-semibold backdrop-blur-md ${size === 'compact' ? 'px-1.5 py-0.5 text-[0.6rem]' : 'px-2.5 py-1 text-xs'} ${className}`}
      {...rest}
    >
      {children}
    </span>
  );
}

interface GradientPageShellProps {
  children: ReactNode;
  contentClassName?: string;
  className?: string;
}

export function GradientPageShell({ children, contentClassName = '', className = '' }: GradientPageShellProps) {
  return (
    <div className={`relative min-h-screen overflow-hidden bg-[#f7f5ff] ${className}`}>
      <div className={`relative z-10 mx-auto max-w-6xl space-y-5 px-6 pt-8 pb-16 ${contentClassName}`}>
        {children}
      </div>
    </div>
  );
}

interface PageLoadingStateProps {
  message: string;
}

export function PageLoadingState({ message }: PageLoadingStateProps) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-[#f7f5ff]">
      <GlassCard className="px-6 py-4">
        <p className="text-sm font-semibold text-slate-700">{message}</p>
      </GlassCard>
    </div>
  );
}
