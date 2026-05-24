import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from 'react';

const glassSurfaceClass =
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
      className={`rounded-2xl px-5 py-3 text-sm font-semibold transition-all hover:scale-[1.03] ${toneClass} disabled:cursor-not-allowed disabled:bg-none disabled:bg-gray-200 disabled:text-gray-500 disabled:shadow-none disabled:hover:scale-100 disabled:hover:bg-gray-200 disabled:hover:brightness-100 ${className}`}
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

interface GradientPageShellProps {
  children: ReactNode;
  contentClassName?: string;
  className?: string;
}

export function GradientPageShell({ children, contentClassName = '', className = '' }: GradientPageShellProps) {
  return (
    <div className={`relative min-h-screen overflow-hidden bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50 ${className}`}>
      <div className="absolute -top-20 -left-20 h-80 w-80 rounded-full bg-linear-to-br from-blue-300 to-purple-400 opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -right-20 -bottom-40 h-96 w-96 rounded-full bg-linear-to-br from-pink-300 to-orange-300 opacity-30 blur-3xl" aria-hidden />
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
    <div className="flex min-h-screen items-center justify-center bg-linear-to-br from-indigo-50 via-purple-50 to-pink-50">
      <GlassCard className="px-6 py-4">
        <p className="text-sm text-gray-700">{message}</p>
      </GlassCard>
    </div>
  );
}
