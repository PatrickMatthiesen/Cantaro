import { normalizeBaseUrl } from '../settings/extensionSettings';
import { isAccessTokenFresh, type ExtensionSession, type ExtensionUser } from './extensionSession';
import { browserSessionRepository, type SessionRepository } from './sessionRepository';
import {
  createPkceChallenge,
  createRandomBase64Url,
  errorMessage,
  parseTokenGrant,
  type TokenGrant,
} from './tokenProtocol';

export interface AuthService {
  beginInteractiveSignIn(apiBaseUrl: string): Promise<ExtensionSession>;
  getAccessToken(apiBaseUrl: string, forceRefresh?: boolean): Promise<string | null>;
  getVerifiedUser(apiBaseUrl: string): Promise<ExtensionUser | null>;
  signOut(apiBaseUrl: string): Promise<void>;
}

interface AuthBrowser {
  runtimeId(): string;
  redirectUrl(path: string): string;
  launchWebAuthFlow(url: string): Promise<string | undefined>;
}

class AuthRequestError extends Error {
  constructor(message: string, readonly invalidatesSession: boolean) {
    super(message);
  }
}

function toSession(apiBaseUrl: string, grant: TokenGrant): ExtensionSession {
  return {
    apiBaseUrl,
    accessToken: grant.accessToken,
    refreshToken: grant.refreshToken,
    accessTokenExpiresAt: new Date(Date.now() + grant.expiresInSeconds * 1000).toISOString(),
    email: grant.email,
  };
}

async function postGrant(
  apiBaseUrl: string,
  params: URLSearchParams,
): Promise<TokenGrant> {
  const response = await fetch(`${apiBaseUrl}/api/auth/extension/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params.toString(),
  });
  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    throw new AuthRequestError(
      errorMessage(payload, 'Extension sign-in failed.'),
      response.status === 400 || response.status === 401 || response.status === 403,
    );
  }
  return parseTokenGrant(payload);
}

async function fetchUser(apiBaseUrl: string, accessToken: string): Promise<ExtensionUser> {
  const response = await fetch(`${apiBaseUrl}/api/auth/me`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    throw new AuthRequestError(
      errorMessage(payload, 'Failed to verify signed-in user.'),
      response.status === 401 || response.status === 403,
    );
  }
  const email = payload && typeof payload === 'object'
    && typeof (payload as Record<string, unknown>).email === 'string'
    ? (payload as { email: string }).email.trim()
    : '';
  if (!email) throw new Error('Signed-in user payload did not include an email address.');
  return { email };
}

function isSessionForApi(session: ExtensionSession | null, apiBaseUrl: string): session is ExtensionSession {
  return Boolean(session && normalizeBaseUrl(session.apiBaseUrl) === apiBaseUrl);
}

async function clearUnchangedRefreshSession(
  repository: SessionRepository,
  apiBaseUrl: string,
  refreshToken: string,
): Promise<void> {
  const unchanged = await repository.read();
  if (isSessionForApi(unchanged, apiBaseUrl) && unchanged.refreshToken === refreshToken) {
    await repository.clear();
  }
}

async function recoverRefreshFailure(
  repository: SessionRepository,
  apiBaseUrl: string,
  session: ExtensionSession,
  error: unknown,
): Promise<string | null> {
  const latest = await repository.read();
  if (isSessionForApi(latest, apiBaseUrl) && latest.refreshToken !== session.refreshToken) {
    return latest.accessToken || null;
  }
  if (error instanceof AuthRequestError && error.invalidatesSession) {
    await clearUnchangedRefreshSession(repository, apiBaseUrl, session.refreshToken);
  }
  return null;
}

type UserVerification =
  | { status: 'verified'; user: ExtensionUser }
  | { status: 'rejected' }
  | { status: 'unavailable' };

async function verifyUser(apiBaseUrl: string, token: string): Promise<UserVerification> {
  try {
    return { status: 'verified', user: await fetchUser(apiBaseUrl, token) };
  } catch (error) {
    return error instanceof AuthRequestError && error.invalidatesSession
      ? { status: 'rejected' }
      : { status: 'unavailable' };
  }
}

async function saveVerifiedEmail(
  repository: SessionRepository,
  user: ExtensionUser,
): Promise<void> {
  const session = await repository.read();
  if (session && session.email !== user.email) {
    await repository.save({ ...session, email: user.email });
  }
}

async function clearRejectedAccessToken(
  repository: SessionRepository,
  apiBaseUrl: string,
  accessToken: string,
): Promise<void> {
  const current = await repository.read();
  if (isSessionForApi(current, apiBaseUrl) && current.accessToken === accessToken) {
    await repository.clear();
  }
}

export function createAuthService(
  repository: SessionRepository,
  authBrowser: AuthBrowser,
): AuthService {
  let refreshInFlight: Promise<string | null> | null = null;

  async function refreshToken(apiBaseUrl: string, session: ExtensionSession): Promise<string | null> {
    if (!session.refreshToken) return session.accessToken || null;
    try {
      const grant = await postGrant(apiBaseUrl, new URLSearchParams({
        grant_type: 'refresh_token',
        refresh_token: session.refreshToken,
        client_id: authBrowser.runtimeId(),
      }));
      const current = await repository.read();
      if (!isSessionForApi(current, apiBaseUrl)
        || current.refreshToken !== session.refreshToken) {
        return current && isSessionForApi(current, apiBaseUrl)
          ? current.accessToken || null
          : null;
      }
      const saved = await repository.save(toSession(apiBaseUrl, grant));
      return saved.accessToken;
    } catch (error) {
      return recoverRefreshFailure(repository, apiBaseUrl, session, error);
    }
  }

  async function getAccessToken(apiBaseUrl: string, forceRefresh = false): Promise<string | null> {
    const normalizedApiBaseUrl = normalizeBaseUrl(apiBaseUrl);
    const session = await repository.read();
    if (!isSessionForApi(session, normalizedApiBaseUrl)) {
      if (session) await repository.clear();
      return null;
    }
    if (!forceRefresh && isAccessTokenFresh(session)) return session.accessToken;

    refreshInFlight ??= refreshToken(normalizedApiBaseUrl, session)
      .finally(() => { refreshInFlight = null; });
    return refreshInFlight;
  }

  return {
    async beginInteractiveSignIn(apiBaseUrl) {
      const normalizedApiBaseUrl = normalizeBaseUrl(apiBaseUrl);
      const redirectUri = authBrowser.redirectUrl('cantaro-auth');
      const state = createRandomBase64Url(24);
      const verifier = createRandomBase64Url(48);
      const authorizeUrl = new URL(`${normalizedApiBaseUrl}/api/auth/extension/authorize`);
      authorizeUrl.searchParams.set('response_type', 'code');
      authorizeUrl.searchParams.set('client_id', authBrowser.runtimeId());
      authorizeUrl.searchParams.set('redirect_uri', redirectUri);
      authorizeUrl.searchParams.set('state', state);
      authorizeUrl.searchParams.set('code_challenge', await createPkceChallenge(verifier));
      authorizeUrl.searchParams.set('code_challenge_method', 'S256');

      const callbackValue = await authBrowser.launchWebAuthFlow(authorizeUrl.toString());
      if (!callbackValue) throw new Error('The browser did not return an authorization callback URL.');
      const callback = new URL(callbackValue);
      const callbackError = callback.searchParams.get('error_description')
        || callback.searchParams.get('error');
      if (callbackError) throw new Error(callbackError);
      const code = callback.searchParams.get('code')?.trim();
      if (!code || callback.searchParams.get('state') !== state) {
        throw new Error('The authorization callback was invalid or did not match the original request.');
      }

      const grant = await postGrant(normalizedApiBaseUrl, new URLSearchParams({
        grant_type: 'authorization_code',
        code,
        client_id: authBrowser.runtimeId(),
        redirect_uri: redirectUri,
        code_verifier: verifier,
      }));
      return repository.save(toSession(normalizedApiBaseUrl, grant));
    },

    getAccessToken,

    async getVerifiedUser(apiBaseUrl) {
      const normalizedApiBaseUrl = normalizeBaseUrl(apiBaseUrl);
      const token = await getAccessToken(normalizedApiBaseUrl);
      if (!token) return null;
      const first = await verifyUser(normalizedApiBaseUrl, token);
      if (first.status === 'verified') {
        await saveVerifiedEmail(repository, first.user);
        return first.user;
      }
      if (first.status === 'unavailable') return null;

      const refreshedToken = await getAccessToken(normalizedApiBaseUrl, true);
      if (!refreshedToken) return null;
      const second = await verifyUser(normalizedApiBaseUrl, refreshedToken);
      if (second.status === 'verified') return second.user;
      if (second.status === 'rejected') {
        await clearRejectedAccessToken(repository, normalizedApiBaseUrl, refreshedToken);
      }
      return null;
    },

    async signOut(apiBaseUrl) {
      const normalizedApiBaseUrl = normalizeBaseUrl(apiBaseUrl);
      await refreshInFlight?.catch(() => null);
      const session = await repository.read();
      if (isSessionForApi(session, normalizedApiBaseUrl) && session.refreshToken) {
        await fetch(`${normalizedApiBaseUrl}/api/auth/extension/revoke`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: new URLSearchParams({
            client_id: authBrowser.runtimeId(),
            refresh_token: session.refreshToken,
          }).toString(),
        }).catch(() => null);
      }
      await repository.clear();
    },
  };
}

export const browserAuthService = createAuthService(browserSessionRepository, {
  runtimeId: () => browser.runtime.id,
  redirectUrl: (path) => browser.identity.getRedirectURL(path),
  launchWebAuthFlow: (url) => browser.identity.launchWebAuthFlow({ interactive: true, url }),
});
