export interface TokenGrant {
  accessToken: string;
  refreshToken: string;
  expiresInSeconds: number;
  email: string;
}
function stringValue(record: Record<string, unknown>, keys: string[]): string {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.trim()) return value.trim();
  }
  return '';
}

export function errorMessage(payload: unknown, fallback: string): string {
  if (!payload || typeof payload !== 'object') return fallback;
  const record = payload as Record<string, unknown>;
  return stringValue(record, ['error_description', 'message', 'error', 'detail', 'title']) || fallback;
}

export function parseTokenGrant(payload: unknown): TokenGrant {
  if (!payload || typeof payload !== 'object') {
    throw new Error('Cantaro API did not return an extension token response.');
  }

  const record = payload as Record<string, unknown>;
  const user = record.user && typeof record.user === 'object'
    ? record.user as Record<string, unknown>
    : {};
  const grant: TokenGrant = {
    accessToken: stringValue(record, ['access_token']),
    refreshToken: stringValue(record, ['refresh_token']),
    expiresInSeconds: typeof record.expires_in === 'number' ? record.expires_in : 0,
    email: stringValue(user, ['email']),
  };

  if (!grant.accessToken || !grant.refreshToken || grant.expiresInSeconds <= 0) {
    throw new Error('Cantaro API did not return the expected extension session payload.');
  }
  return grant;
}

function encodeBase64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

export function createRandomBase64Url(byteLength: number): string {
  return encodeBase64Url(crypto.getRandomValues(new Uint8Array(byteLength)));
}

export async function createPkceChallenge(codeVerifier: string): Promise<string> {
  const bytes = new TextEncoder().encode(codeVerifier);
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return encodeBase64Url(new Uint8Array(digest));
}
