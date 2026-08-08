import { describe, expect, it } from 'vitest';
import { errorMessage, parseTokenGrant } from './tokenProtocol';

describe('tokenProtocol', () => {
  it('accepts the API OAuth wire format', () => {
    expect(parseTokenGrant({
      access_token: 'access',
      refresh_token: 'refresh',
      expires_in: 3600,
      user: { email: 'user@example.test' },
    })).toEqual({
      accessToken: 'access',
      refreshToken: 'refresh',
      expiresInSeconds: 3600,
      email: 'user@example.test',
    });
  });

  it('rejects incomplete token responses', () => {
    expect(() => parseTokenGrant({ access_token: 'access' }))
      .toThrow('expected extension session payload');
  });

  it('rejects non-contract token field aliases', () => {
    expect(() => parseTokenGrant({
      accessToken: 'access',
      refreshToken: 'refresh',
      expiresIn: 3600,
    })).toThrow('expected extension session payload');
  });

  it('uses structured API error details', () => {
    expect(errorMessage({ error_description: 'Access denied.' }, 'fallback'))
      .toBe('Access denied.');
  });
});
