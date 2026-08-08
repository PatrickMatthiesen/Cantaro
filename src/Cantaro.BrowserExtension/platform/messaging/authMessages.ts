import type { ExtensionUser } from '../auth/extensionSession';

export type AuthBackgroundRequest =
  | {
    type: 'auth.token.get';
    correlationId: string;
    payload: { apiBaseUrl: string; forceRefresh: boolean };
  }
  | {
    type: 'auth.session.verify';
    correlationId: string;
    payload: { apiBaseUrl: string };
  }
  | {
    type: 'auth.session.signOut';
    correlationId: string;
    payload: { apiBaseUrl: string };
  };

export type AuthBackgroundResponse =
  | { accessToken: string | null }
  | { user: ExtensionUser | null }
  | { signedOut: true };

export function isAuthBackgroundRequest(value: unknown): value is AuthBackgroundRequest {
  if (!value || typeof value !== 'object') return false;
  const request = value as { type?: unknown; correlationId?: unknown; payload?: unknown };
  if (request.type !== 'auth.token.get'
    && request.type !== 'auth.session.verify'
    && request.type !== 'auth.session.signOut') return false;
  if (typeof request.correlationId !== 'string') return false;
  if (!request.payload || typeof request.payload !== 'object') return false;
  const payload = request.payload as { apiBaseUrl?: unknown; forceRefresh?: unknown };
  if (typeof payload.apiBaseUrl !== 'string') return false;
  return request.type !== 'auth.token.get' || typeof payload.forceRefresh === 'boolean';
}
