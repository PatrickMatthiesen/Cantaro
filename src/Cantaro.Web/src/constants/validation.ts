import { z } from 'zod';

/**
 * Validation constants for form inputs.
 * These should match the backend validation rules.
 */

export const PASSWORD_MIN_LENGTH = 6;
export const PASSWORD_MIN_UNIQUE_CHARS = 1;

/**
 * Zod validation schemas for authentication forms
 */

const passwordValidation = z
  .string()
  .min(PASSWORD_MIN_LENGTH, `Password must be at least ${PASSWORD_MIN_LENGTH} characters long`)
  .refine((password) => {
    const uniqueChars = new Set(password).size;
    return uniqueChars >= PASSWORD_MIN_UNIQUE_CHARS;
  }, `Password must contain at least ${PASSWORD_MIN_UNIQUE_CHARS} unique character(s)`);

export const loginSchema = z.object({
  email: z.string().email('Invalid email address'),
  password: passwordValidation,
});

export const registerSchema = z.object({
  email: z.string().email('Invalid email address'),
  password: passwordValidation,
  confirmPassword: z.string(),
}).refine((data) => data.password === data.confirmPassword, {
  message: "Passwords do not match",
  path: ["confirmPassword"],
});

export type LoginFormData = z.infer<typeof loginSchema>;
export type RegisterFormData = z.infer<typeof registerSchema>;
