import { useState } from 'react';
import type { FormEvent } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { PASSWORD_MIN_LENGTH, registerSchema, type RegisterFormData } from '../constants/validation';

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
    <div className="space-y-6 text-left text-white">
      <div className="space-y-2">
        <p className="text-xs uppercase tracking-[0.5em] text-slate-400">Create account</p>
        <h2 className="text-3xl font-semibold">Join Cantaro</h2>
        <p className="text-sm text-slate-400">Provision your secure workspace in a few seconds.</p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-5">
        <div className="space-y-2">
          <label htmlFor="email" className="text-sm font-medium text-slate-200">
            Email
          </label>
          <input
            type="email"
            id="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
            className="w-full rounded-2xl border border-white/15 bg-white/5 px-4 py-3 text-base text-white placeholder:text-slate-500 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-500/30"
            placeholder="you@example.com"
          />
        </div>

        <div className="space-y-2">
          <label htmlFor="password" className="text-sm font-medium text-slate-200">
            Password
          </label>
          <input
            type="password"
            id="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={PASSWORD_MIN_LENGTH}
            autoComplete="new-password"
            className="w-full rounded-2xl border border-white/15 bg-white/5 px-4 py-3 text-base text-white placeholder:text-slate-500 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-500/30"
            placeholder="••••••••"
          />
        </div>

        <div className="space-y-2">
          <label htmlFor="confirmPassword" className="text-sm font-medium text-slate-200">
            Confirm password
          </label>
          <input
            type="password"
            id="confirmPassword"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
            minLength={PASSWORD_MIN_LENGTH}
            autoComplete="new-password"
            className="w-full rounded-2xl border border-white/15 bg-white/5 px-4 py-3 text-base text-white placeholder:text-slate-500 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-500/30"
            placeholder="Repeat password"
          />
        </div>

        {error && (
          <div className="rounded-2xl border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-200">
            {error}
          </div>
        )}

        <button
          type="submit"
          disabled={isLoading}
          className="inline-flex w-full items-center justify-center rounded-2xl bg-gradient-to-r from-brand-400 to-fuchsia-500 px-4 py-3 text-base font-semibold text-white shadow-[0_20px_60px_rgba(168,85,247,0.35)] transition hover:translate-y-0.5 hover:shadow-[0_30px_80px_rgba(168,85,247,0.45)] disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isLoading ? 'Creating account…' : 'Create secure account'}
        </button>
      </form>

      <p className="text-center text-sm text-slate-400">
        Already registered?{' '}
        <button
          type="button"
          onClick={onSwitchToLogin}
          className="font-semibold text-white underline-offset-4 transition hover:text-brand-200"
        >
          Back to sign in
        </button>
      </p>
    </div>
  );
};
