import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from 'react';

interface AuthFormHeaderProps {
  eyebrow: string;
  title: string;
  description: string;
}

export function AuthFormHeader({ eyebrow, title, description }: AuthFormHeaderProps) {
  return (
    <div className="space-y-2">
      <p className="text-xs tracking-[0.4em] text-gray-500 uppercase">{eyebrow}</p>
      <h2 className="text-3xl font-semibold">{title}</h2>
      <p className="text-sm text-gray-600">{description}</p>
    </div>
  );
}

interface AuthInputFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
}

export function AuthInputField({ id, label, className = '', ...rest }: AuthInputFieldProps) {
  return (
    <div className="space-y-2">
      <label htmlFor={id} className="text-sm font-medium text-gray-700">
        {label}
      </label>
      <input
        id={id}
        className={`w-full rounded-2xl border border-gray-200 bg-white px-4 py-3 text-base text-gray-900 placeholder:text-gray-400 focus:border-indigo-400 focus:ring-2 focus:ring-indigo-500/20 focus:outline-none ${className}`}
        {...rest}
      />
    </div>
  );
}

interface AuthErrorBannerProps {
  message: string;
}

export function AuthErrorBanner({ message }: AuthErrorBannerProps) {
  return (
    <div className="rounded-2xl border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-700">
      {message}
    </div>
  );
}

interface AuthSubmitButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  idleLabel: string;
  loadingLabel: string;
  isLoading: boolean;
}

export function AuthSubmitButton({
  idleLabel,
  loadingLabel,
  isLoading,
  className = '',
  ...rest
}: AuthSubmitButtonProps) {
  return (
    <button
      type="submit"
      disabled={isLoading}
      className={`inline-flex w-full items-center justify-center rounded-2xl bg-linear-to-r from-indigo-500 to-purple-500 px-4 py-3 text-base font-semibold text-white shadow-[0_18px_40px_rgba(99,102,241,0.35)] transition hover:translate-y-0.5 hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-60 ${className}`}
      {...rest}
    >
      {isLoading ? loadingLabel : idleLabel}
    </button>
  );
}

interface AuthSwitchPromptProps {
  prompt: ReactNode;
  actionLabel: string;
  onAction: () => void;
}

export function AuthSwitchPrompt({ prompt, actionLabel, onAction }: AuthSwitchPromptProps) {
  return (
    <p className="text-center text-sm text-gray-600">
      {prompt}{' '}
      <button
        type="button"
        onClick={onAction}
        className="font-semibold text-indigo-700 underline-offset-4 transition hover:text-indigo-500"
      >
        {actionLabel}
      </button>
    </p>
  );
}