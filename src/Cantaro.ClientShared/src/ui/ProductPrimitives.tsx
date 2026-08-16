import type {
  ButtonHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
} from "react";

export type ActionTone = "personal" | "secondary" | "ghost" | "danger";

const toneClasses: Record<ActionTone, string> = {
  personal:
    "bg-personal-accent text-personal-accent-content hover:bg-personal-accent-hover active:brightness-95",
  secondary:
    "border border-border-strong bg-surface text-content hover:bg-surface-hover active:bg-surface-subtle",
  ghost:
    "bg-transparent text-content-muted hover:bg-surface-hover hover:text-content active:bg-surface-subtle",
  danger:
    "bg-danger-action text-danger-action-content hover:bg-danger-action-hover active:brightness-95",
};

export function actionClassName({
  tone = "secondary",
  fullWidth = false,
  className = "",
}: {
  tone?: ActionTone;
  fullWidth?: boolean;
  className?: string;
} = {}) {
  return `inline-flex min-h-11 items-center justify-center gap-2 px-4 text-sm font-bold transition-colors duration-200 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-45 ${toneClasses[tone]} ${fullWidth ? "w-full" : ""} ${className}`;
}

export interface ActionButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  tone?: ActionTone;
  fullWidth?: boolean;
  busyLabel?: string;
}

export function ActionButton({
  tone = "secondary",
  fullWidth = false,
  busyLabel,
  className = "",
  children,
  ...props
}: ActionButtonProps) {
  const isBusy = props["aria-busy"] === true;
  return (
    <button
      className={actionClassName({ tone, fullWidth, className })}
      {...props}
    >
      {isBusy && busyLabel ? busyLabel : children}
    </button>
  );
}

export interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  label: string;
  children: ReactNode;
}

export function IconButton({
  label,
  className = "",
  children,
  ...props
}: IconButtonProps) {
  return (
    <button
      aria-label={label}
      title={props.title ?? label}
      className={`inline-flex size-11 shrink-0 items-center justify-center text-content-muted transition-colors duration-200 hover:bg-surface-hover hover:text-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-40 ${className}`}
      {...props}
    >
      {children}
    </button>
  );
}

export interface SelectFieldProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  visuallyHiddenLabel?: boolean;
  containerClassName?: string;
}

export function SelectField({
  label,
  visuallyHiddenLabel = false,
  containerClassName = "",
  className = "",
  children,
  ...props
}: SelectFieldProps) {
  return (
    <label
      className={`grid gap-1.5 text-sm text-content-muted ${containerClassName}`}
    >
      <span className={visuallyHiddenLabel ? "sr-only" : undefined}>
        {label}
      </span>
      <select
        className={`min-h-11 border border-border-strong bg-surface px-3 font-semibold text-content outline-none transition-colors hover:bg-surface-hover focus:border-focus focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus disabled:cursor-not-allowed disabled:opacity-45 ${className}`}
        {...props}
      >
        {children}
      </select>
    </label>
  );
}
