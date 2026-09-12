import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { loginSchema, type LoginFormData } from '@cantaro/client-shared/auth';
import {
  AuthEmailField,
  AuthFormLayout,
  AuthPasswordField,
  TurnstileWidget,
} from '@cantaro/client-shared/auth';
import { submitAuthForm } from '@cantaro/client-shared/auth';
import { useAuthTurnstile } from '../hooks/useAuthTurnstile';

interface LoginFormProps {
  onSwitchToRegister: () => void;
}

export const LoginForm = ({ onSwitchToRegister }: LoginFormProps) => {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const { token: turnstileToken, guardSubmission, reset: resetTurnstile, widgetProps } = useAuthTurnstile('login');

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    if (guardSubmission(e, setError)) return;

    const formData: LoginFormData = { email, password };
    await submitAuthForm({
      event: e,
      formData,
      schema: loginSchema,
      submit: () => login({ email, password, turnstileToken: turnstileToken ?? undefined }),
      setError,
      setIsLoading,
      fallbackMessage: 'Login failed',
      onSettled: resetTurnstile,
    });
  };

  return (
    <AuthFormLayout
      eyebrow="Sign in"
      title="Welcome back"
      description="Open Cantaro and continue syncing playlists."
      onSubmit={handleSubmit}
      error={error}
      submitLabel="Sign in"
      submittingLabel="Signing in…"
      isLoading={isLoading}
      switchPrompt="Don't have an account?"
      switchActionLabel="Register instead"
      onSwitchAction={onSwitchToRegister}
    >
      <AuthEmailField value={email} onChange={(e) => setEmail(e.target.value)} />
      <AuthPasswordField
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        autoComplete="current-password"
      />
      <TurnstileWidget {...widgetProps} />
    </AuthFormLayout>
  );
};
