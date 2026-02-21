import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { PASSWORD_MIN_LENGTH, loginSchema, type LoginFormData } from '../constants/validation';

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
      <div className="space-y-2">
        <p className="text-xs tracking-[0.4em] text-gray-500 uppercase">Sign in</p>
        <h2 className="text-3xl font-semibold">Welcome back</h2>
        <p className="text-sm text-gray-600">Open your workspace and continue syncing playlists.</p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-5">
        <div className="space-y-2">
          <label htmlFor="email" className="text-sm font-medium text-gray-700">
            Email
          </label>
          <input
            type="email"
            id="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
            className="w-full rounded-2xl border border-gray-200 bg-white px-4 py-3 text-base text-gray-900 placeholder:text-gray-400 focus:border-indigo-400 focus:ring-2 focus:ring-indigo-500/20 focus:outline-none"
            placeholder="you@example.com"
          />
        </div>

        <div className="space-y-2">
          <label htmlFor="password" className="text-sm font-medium text-gray-700">
            Password
          </label>
          <input
            type="password"
            id="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={PASSWORD_MIN_LENGTH}
            autoComplete="current-password"
            className="w-full rounded-2xl border border-gray-200 bg-white px-4 py-3 text-base text-gray-900 placeholder:text-gray-400 focus:border-indigo-400 focus:ring-2 focus:ring-indigo-500/20 focus:outline-none"
            placeholder="••••••••"
          />
        </div>

        {error && (
          <div className="rounded-2xl border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-700">
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={isLoading}
          className="inline-flex w-full items-center justify-center rounded-2xl bg-linear-to-r from-indigo-500 to-purple-500 px-4 py-3 text-base font-semibold text-white shadow-[0_18px_40px_rgba(99,102,241,0.35)] transition hover:translate-y-0.5 hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isLoading ? 'Signing in…' : 'Sign in'}
        </button>
      </form>

      <p className="text-center text-sm text-gray-600">
        Don't have an account?{' '}
        <button
          type="button"
          onClick={onSwitchToRegister}
          className="font-semibold text-indigo-700 underline-offset-4 transition hover:text-indigo-500"
        >
          Register instead
        </button>
      </p>
    </div>
  );
};
