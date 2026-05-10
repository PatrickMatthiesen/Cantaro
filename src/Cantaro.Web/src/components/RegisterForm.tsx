import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { PASSWORD_MIN_LENGTH, registerSchema, type RegisterFormData } from '../constants/validation';
import {
  AuthEmailField,
  AuthFormLayout,
  AuthInputField,
  AuthPasswordField,
} from './AuthFormShared';
import { submitAuthForm } from './authFormSubmit';

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

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    const formData: RegisterFormData = { email, password, confirmPassword };
    await submitAuthForm({
      event: e,
      formData,
      schema: registerSchema,
      submit: () => register({ email, password }),
      setError,
      setIsLoading,
      fallbackMessage: 'Registration failed',
      onSuccess: onSwitchToLogin,
    });
  };

  return (
    <AuthFormLayout
      eyebrow="Create account"
      title="Create your workspace"
      description="Set up an account to connect services and sync playlists."
      onSubmit={handleSubmit}
      error={error}
      submitLabel="Create account"
      submittingLabel="Creating account…"
      isLoading={isLoading}
      switchPrompt="Already registered?"
      switchActionLabel="Back to sign in"
      onSwitchAction={onSwitchToLogin}
    >
      <AuthEmailField value={email} onChange={(e) => setEmail(e.target.value)} />
      <AuthPasswordField
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        autoComplete="new-password"
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
    </AuthFormLayout>
  );
};
