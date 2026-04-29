import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { PASSWORD_MIN_LENGTH, registerSchema, type RegisterFormData } from '../constants/validation';
import {
  AuthErrorBanner,
  AuthFormHeader,
  AuthInputField,
  AuthSubmitButton,
  AuthSwitchPrompt,
} from './AuthFormShared';

interface RegisterFormProps {
  onSwitchToLogin: () => void;
}

export const RegisterForm = ({ onSwitchToLogin }: RegisterFormProps) => {
  const { register } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');

    // Validate form data with Zod
    const formData: RegisterFormData = { email, password, confirmPassword };
    const result = registerSchema.safeParse(formData);

    if (!result.success) {
      // Get the first error message
      const firstError = result.error.issues[0];
      setError(firstError.message);
      return;
    }

    setIsLoading(true);

    try {
      await register({ email, password });
      onSwitchToLogin();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Registration failed');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="space-y-6 text-left text-gray-900">
      <AuthFormHeader
        eyebrow="Create account"
        title="Create your workspace"
        description="Set up an account to connect services and sync playlists."
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
          autoComplete="new-password"
          placeholder="••••••••"
        />

        <AuthInputField
          type="password"
          id="confirmPassword"
          label="Confirm password"
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          required
          minLength={PASSWORD_MIN_LENGTH}
          autoComplete="new-password"
          placeholder="Repeat password"
        />

        {error && (
          <AuthErrorBanner message={error} />
        )}

        <AuthSubmitButton
          isLoading={isLoading}
          idleLabel="Create account"
          loadingLabel="Creating account…"
        />
      </form>

      <AuthSwitchPrompt
        prompt="Already registered?"
        actionLabel="Back to sign in"
        onAction={onSwitchToLogin}
      />
    </div>
  );
};
