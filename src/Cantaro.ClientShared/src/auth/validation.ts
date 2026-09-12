import { z } from 'zod';

/**
 * Validation constants for form inputs.
 * These should match the backend validation rules.
 */

export const PASSWORD_MIN_LENGTH = 15;
const PASSWORD_MIN_UNIQUE_CHARS = 1;

/**
 * Email regex that requires a valid TLD (at least 2 characters).
 * This matches the backend EmailWithTldAttribute validation.
 * Examples: user@example.com ✓, user@domain.dk ✓, user@local ✗
 */
const EMAIL_WITH_TLD_REGEX = /^[^@\s]+@[^@\s]+\.[a-zA-Z]{2,}$/i;

/**
 * Zod validation schemas for authentication forms
 */

const emailValidation = z
  .string()
  .regex(EMAIL_WITH_TLD_REGEX, 'Invalid email address. Email must include a valid domain with a TLD (e.g., example@domain.com)');

const passwordValidation = z
  .string()
  .min(PASSWORD_MIN_LENGTH, `Password must be at least ${PASSWORD_MIN_LENGTH} characters long`)
  .refine((password) => {
    const uniqueChars = new Set(password).size;
    return uniqueChars >= PASSWORD_MIN_UNIQUE_CHARS;
  }, `Password must contain at least ${PASSWORD_MIN_UNIQUE_CHARS} unique character(s)`);

export const loginSchema = z.object({
  email: emailValidation,
  // Existing passwords remain usable when the policy for new passwords changes.
  password: z.string().min(1, 'Enter your password'),
});

export const registerSchema = z.object({
  email: emailValidation,
  password: passwordValidation,
  confirmPassword: z.string(),
}).refine((data) => data.password === data.confirmPassword, {
  message: "Passwords do not match",
  path: ["confirmPassword"],
});

export type LoginFormData = z.infer<typeof loginSchema>;
export type RegisterFormData = z.infer<typeof registerSchema>;
