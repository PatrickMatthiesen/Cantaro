import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { PASSWORD_MIN_LENGTH, loginSchema, type LoginFormData } from '../constants/validation';
import {
  AuthErrorBanner,
  AuthFormHeader,
  AuthInputField,
  AuthSubmitButton,
  AuthSwitchPrompt,
} from './AuthFormShared';

interface LoginFormProps {
  onSwitchToRegister: () => void;
}

export const LoginForm = ({ onSwitchToRegister }: LoginFormProps) => {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');

    // Validate form data with Zod
    const formData: LoginFormData = { email, password };
    const result = loginSchema.safeParse(formData);

    if (!result.success) {
      // Get the first error message
      const firstError = result.error.issues[0];
      setError(firstError.message);
      return;
    }

    setIsLoading(true);

    try {
      await login({ email, password });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="space-y-6 text-left text-gray-900">
      <AuthFormHeader
        eyebrow="Sign in"
        title="Welcome back"
        description="Open your workspace and continue syncing playlists."
      />

      <form onSubmit={handleSubmit} className="space-y-5">
        <AuthInputField
          type="email"
          id="email"
          label="Email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          autoComplete="email"
          placeholder="you@example.com"
        />

        <AuthInputField
          type="password"
          id="password"
          label="Password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          minLength={PASSWORD_MIN_LENGTH}
          autoComplete="current-password"
          placeholder="••••••••"
        />

        {error && (
          <AuthErrorBanner message={error} />
        )}

        <AuthSubmitButton
          isLoading={isLoading}
          idleLabel="Sign in"
          loadingLabel="Signing in…"
        />
      </form>

      <AuthSwitchPrompt
        prompt="Don't have an account?"
        actionLabel="Register instead"
        onAction={onSwitchToRegister}
      />
    </div>
  );
};
