import type { ButtonHTMLAttributes, HTMLAttributes, ReactNode } from 'react';

const glassSurfaceClass = 'border border-border-subtle bg-surface';

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
      className={`relative overflow-hidden ${glassSurfaceClass} ${interactive ? 'transition-colors duration-200 hover:bg-surface-hover' : ''
        } ${className}`}
      {...rest}
    >
      {hoverGradient ? (
        <span
          className={`pointer-events-none absolute inset-0 bg-linear-to-br ${hoverGradient} opacity-0 transition-opacity duration-200 group-hover:opacity-10`}
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
      ? 'bg-action text-action-content hover:bg-action-hover'
      : tone === 'soft'
        ? 'border border-border-strong bg-surface text-content hover:bg-surface-hover'
        : 'bg-personal-accent text-personal-accent-content hover:bg-personal-accent-hover';

  return (
    <button
      className={`inline-flex min-h-11 items-center justify-center gap-2 px-4 text-sm font-bold transition-colors duration-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus ${toneClass} disabled:cursor-not-allowed disabled:bg-surface-subtle disabled:text-content-subtle disabled:hover:bg-surface-subtle ${className}`}
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
    connected: 'border-success-border bg-success-surface text-success-content',
    available: 'border-info-border bg-info-surface text-info-content',
    warning: 'border-warning-border bg-warning-surface text-warning-content',
  } as const;

  return (
    <span className={`inline-flex items-center border px-2.5 py-1 text-xs font-semibold ${map[status]}`}>
      {status}
    </span>
  );
}

interface PillProps extends HTMLAttributes<HTMLSpanElement> {
  tone?: 'neutral' | 'info' | 'success' | 'warning';
  size?: 'compact' | 'regular';
}

export function Pill({ tone = 'neutral', size = 'regular', className = '', children, ...rest }: PillProps) {
  const toneClass = {
    neutral: 'border-border-subtle bg-surface-subtle text-content-muted',
    info: 'border-info-border bg-info-surface text-info-content',
    success: 'border-success-border bg-success-surface text-success-content',
    warning: 'border-warning-border bg-warning-surface text-warning-content',
  }[tone];

  return (
    <span
      className={`inline-flex items-center border font-semibold ${toneClass} ${size === 'compact' ? 'px-1.5 py-0.5 text-[0.6rem]' : 'px-2.5 py-1 text-xs'} ${className}`}
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
    <div className={`min-h-screen bg-canvas ${className}`}>
      <div className={`mx-auto max-w-6xl space-y-5 px-4 pt-6 pb-16 sm:px-7 ${contentClassName}`}>
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
    <div className="flex min-h-screen items-center justify-center bg-canvas">
      <div className="border-y border-border-subtle px-6 py-5">
        <p className="text-sm font-semibold text-content">{message}</p>
      </div>
    </div>
  );
}
