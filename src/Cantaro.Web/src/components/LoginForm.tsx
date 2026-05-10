import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { loginSchema, type LoginFormData } from '../constants/validation';
import {
  AuthEmailField,
  AuthFormLayout,
  AuthPasswordField,
} from './AuthFormShared';
import { submitAuthForm } from './authFormSubmit';

interface LoginFormProps {
  onSwitchToRegister: () => void;
}

export const LoginForm = ({ onSwitchToRegister }: LoginFormProps) => {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    const formData: LoginFormData = { email, password };
    await submitAuthForm({
      event: e,
      formData,
      schema: loginSchema,
      submit: () => login({ email, password }),
      setError,
      setIsLoading,
      fallbackMessage: 'Login failed',
    });
  };

  return (
    <AuthFormLayout
      eyebrow="Sign in"
      title="Welcome back"
      description="Open your workspace and continue syncing playlists."
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
    </AuthFormLayout>
  );
};
