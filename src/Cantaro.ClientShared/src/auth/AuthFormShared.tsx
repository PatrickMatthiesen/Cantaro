import type { ButtonHTMLAttributes, ChangeEventHandler, FormEvent, InputHTMLAttributes, ReactNode } from 'react';
import { PASSWORD_MIN_LENGTH } from './validation';

interface AuthFormHeaderProps {
  eyebrow: string;
  title: string;
  description: string;
}

function AuthFormHeader({ eyebrow, title, description }: AuthFormHeaderProps) {
  return (
    <div className="space-y-2">
      <p className="text-xs tracking-[0.4em] text-content-muted uppercase">{eyebrow}</p>
      <h2 className="text-3xl font-semibold">{title}</h2>
      <p className="text-sm text-content-muted">{description}</p>
    </div>
  );
}

interface AuthInputFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
}

export function AuthInputField({ id, label, className = '', ...rest }: AuthInputFieldProps) {
  return (
    <div className="space-y-2">
      <label htmlFor={id} className="text-sm font-medium text-content">
        {label}
      </label>
      <input
        id={id}
        className={`min-h-12 w-full border border-border-subtle bg-surface px-4 text-base text-content placeholder:text-content-subtle focus:border-focus focus:outline-none ${className}`}
        {...rest}
      />
    </div>
  );
}

interface AuthEmailFieldProps {
  value: string;
  onChange: ChangeEventHandler<HTMLInputElement>;
}

export function AuthEmailField({ value, onChange }: AuthEmailFieldProps) {
  return (
    <AuthInputField
      type="email"
      id="email"
      label="Email"
      value={value}
      onChange={onChange}
      required
      autoComplete="email"
      placeholder="you@example.com"
    />
  );
}

interface AuthErrorBannerProps {
  message: string;
}

function AuthErrorBanner({ message }: AuthErrorBannerProps) {
  return (
    <div className="border-y border-danger-border bg-danger-surface px-4 py-3 text-sm text-danger-content">
      {message}
    </div>
  );
}

interface AuthSubmitButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  idleLabel: string;
  loadingLabel: string;
  isLoading: boolean;
}

function AuthSubmitButton({
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
      className={`inline-flex min-h-12 w-full items-center justify-center bg-personal-accent px-4 text-base font-semibold text-personal-accent-content transition hover:bg-personal-accent-hover disabled:cursor-not-allowed disabled:opacity-60 ${className}`}
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

function AuthSwitchPrompt({ prompt, actionLabel, onAction }: AuthSwitchPromptProps) {
  return (
    <p className="text-center text-sm text-content-muted">
      {prompt}{' '}
      <button
        type="button"
        onClick={onAction}
        className="font-semibold text-content underline decoration-personal-accent underline-offset-4 transition hover:text-personal-accent-strong"
      >
        {actionLabel}
      </button>
    </p>
  );
}

interface AuthFormLayoutProps {
  eyebrow: string;
  title: string;
  description: string;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void | Promise<void>;
  children: ReactNode;
  error?: string;
  submitLabel: string;
  submittingLabel: string;
  isLoading: boolean;
  switchPrompt: ReactNode;
  switchActionLabel: string;
  onSwitchAction: () => void;
}

export function AuthFormLayout({
  eyebrow,
  title,
  description,
  onSubmit,
  children,
  error,
  submitLabel,
  submittingLabel,
  isLoading,
  switchPrompt,
  switchActionLabel,
  onSwitchAction,
}: AuthFormLayoutProps) {
  return (
    <div className="space-y-6 text-left text-content">
      <AuthFormHeader eyebrow={eyebrow} title={title} description={description} />

      <form onSubmit={onSubmit} className="space-y-5">
        {children}

        {error ? <AuthErrorBanner message={error} /> : null}

        <AuthSubmitButton
          isLoading={isLoading}
          idleLabel={submitLabel}
          loadingLabel={submittingLabel}
        />
      </form>

      <AuthSwitchPrompt
        prompt={switchPrompt}
        actionLabel={switchActionLabel}
        onAction={onSwitchAction}
      />
    </div>
  );
}

interface AuthPasswordFieldProps {
  value: string;
  onChange: ChangeEventHandler<HTMLInputElement>;
  autoComplete: 'current-password' | 'new-password';
}

export function AuthPasswordField({ value, onChange, autoComplete }: AuthPasswordFieldProps) {
  return (
    <AuthInputField
      type="password"
      id="password"
      label="Password"
      value={value}
      onChange={onChange}
      required
      minLength={autoComplete === 'new-password' ? PASSWORD_MIN_LENGTH : 1}
      autoComplete={autoComplete}
      placeholder="••••••••"
    />
  );
}
