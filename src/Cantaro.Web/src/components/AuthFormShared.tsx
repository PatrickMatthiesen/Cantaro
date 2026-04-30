import type { ButtonHTMLAttributes, ChangeEventHandler, FormEvent, InputHTMLAttributes, ReactNode } from 'react';
import type { ZodType } from 'zod';

interface AuthFormHeaderProps {
  eyebrow: string;
  title: string;
  description: string;
}

function AuthFormHeader({ eyebrow, title, description }: AuthFormHeaderProps) {
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

function AuthSwitchPrompt({ prompt, actionLabel, onAction }: AuthSwitchPromptProps) {
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
    <div className="space-y-6 text-left text-gray-900">
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
      minLength={6}
      autoComplete={autoComplete}
      placeholder="••••••••"
    />
  );
}

interface SubmitAuthFormOptions<TFormData> {
  event: FormEvent<HTMLFormElement>;
  formData: TFormData;
  schema: ZodType<TFormData>;
  submit: () => Promise<void>;
  setError: (message: string) => void;
  setIsLoading: (isLoading: boolean) => void;
  fallbackMessage: string;
  onSuccess?: () => void | Promise<void>;
}

function getValidationError<TFormData>(schema: ZodType<TFormData>, formData: TFormData): string | null {
  const result = schema.safeParse(formData);
  if (result.success) {
    return null;
  }

  return result.error.issues[0]?.message ?? 'Invalid form data';
}

function getSubmissionErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

export async function submitAuthForm<TFormData>({
  event,
  formData,
  schema,
  submit,
  setError,
  setIsLoading,
  fallbackMessage,
  onSuccess,
}: SubmitAuthFormOptions<TFormData>) {
  event.preventDefault();
  setError('');

  const validationError = getValidationError(schema, formData);
  if (validationError) {
    setError(validationError);
    return;
  }

  setIsLoading(true);

  try {
    await submit();
    await onSuccess?.();
  } catch (error) {
    setError(getSubmissionErrorMessage(error, fallbackMessage));
  } finally {
    setIsLoading(false);
  }
}