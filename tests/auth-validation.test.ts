import { describe, expect, test } from 'bun:test';
import { loginSchema, registerSchema } from '../src/Cantaro.ClientShared/src/auth/validation';

describe('password policy migration', () => {
  test('existing short passwords can still be used to sign in', () => {
    expect(loginSchema.safeParse({ email: 'person@example.com', password: 'old123' }).success).toBe(true);
  });

  test('registration rejects short passwords and accepts long passphrases without symbols', () => {
    const request = (password: string) => ({ email: 'person@example.com', password, confirmPassword: password });
    expect(registerSchema.safeParse(request('old123')).success).toBe(false);
    expect(registerSchema.safeParse(request('a long memorable passphrase')).success).toBe(true);
  });
});
