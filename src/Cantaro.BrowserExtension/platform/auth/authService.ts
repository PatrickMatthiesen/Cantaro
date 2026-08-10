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
  beginInteractiveSignIn(baseUrl: string): Promise<ExtensionSession>;
  getAccessToken(baseUrl: string, forceRefresh?: boolean): Promise<string | null>;
  getVerifiedUser(baseUrl: string): Promise<ExtensionUser | null>;
  signOut(baseUrl: string): Promise<void>;
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

function toSession(baseUrl: string, grant: TokenGrant): ExtensionSession {
  return {
    baseUrl,
    accessToken: grant.accessToken,
    refreshToken: grant.refreshToken,
    accessTokenExpiresAt: new Date(Date.now() + grant.expiresInSeconds * 1000).toISOString(),
    email: grant.email,
  };
}

async function postGrant(
  baseUrl: string,
  params: URLSearchParams,
): Promise<TokenGrant> {
  const response = await fetch(`${baseUrl}/api/auth/extension/token`, {
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

async function fetchUser(baseUrl: string, accessToken: string): Promise<ExtensionUser> {
  const response = await fetch(`${baseUrl}/api/auth/me`, {
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

function isSessionForApi(session: ExtensionSession | null, baseUrl: string): session is ExtensionSession {
  return Boolean(session && normalizeBaseUrl(session.baseUrl) === baseUrl);
}

async function clearUnchangedRefreshSession(
  repository: SessionRepository,
  baseUrl: string,
  refreshToken: string,
): Promise<void> {
  const unchanged = await repository.read();
  if (isSessionForApi(unchanged, baseUrl) && unchanged.refreshToken === refreshToken) {
    await repository.clear();
  }
}

async function recoverRefreshFailure(
  repository: SessionRepository,
  baseUrl: string,
  session: ExtensionSession,
  error: unknown,
): Promise<string | null> {
  const latest = await repository.read();
  if (isSessionForApi(latest, baseUrl) && latest.refreshToken !== session.refreshToken) {
    return latest.accessToken || null;
  }
  if (error instanceof AuthRequestError && error.invalidatesSession) {
    await clearUnchangedRefreshSession(repository, baseUrl, session.refreshToken);
  }
  return null;
}

type UserVerification =
  | { status: 'verified'; user: ExtensionUser }
  | { status: 'rejected' }
  | { status: 'unavailable' };

async function verifyUser(baseUrl: string, token: string): Promise<UserVerification> {
  try {
    return { status: 'verified', user: await fetchUser(baseUrl, token) };
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
  baseUrl: string,
  accessToken: string,
): Promise<void> {
  const current = await repository.read();
  if (isSessionForApi(current, baseUrl) && current.accessToken === accessToken) {
    await repository.clear();
  }
}

export function createAuthService(
  repository: SessionRepository,
  authBrowser: AuthBrowser,
): AuthService {
  let refreshInFlight: Promise<string | null> | null = null;

  async function refreshToken(baseUrl: string, session: ExtensionSession): Promise<string | null> {
    if (!session.refreshToken) return session.accessToken || null;
    try {
      const grant = await postGrant(baseUrl, new URLSearchParams({
        grant_type: 'refresh_token',
        refresh_token: session.refreshToken,
        client_id: authBrowser.runtimeId(),
      }));
      const current = await repository.read();
      if (!isSessionForApi(current, baseUrl)
        || current.refreshToken !== session.refreshToken) {
        return current && isSessionForApi(current, baseUrl)
          ? current.accessToken || null
          : null;
      }
      const saved = await repository.save(toSession(baseUrl, grant));
      return saved.accessToken;
    } catch (error) {
      return recoverRefreshFailure(repository, baseUrl, session, error);
    }
  }

  async function getAccessToken(baseUrl: string, forceRefresh = false): Promise<string | null> {
    const normalizedbaseUrl = normalizeBaseUrl(baseUrl);
    const session = await repository.read();
    if (!isSessionForApi(session, normalizedbaseUrl)) {
      if (session) await repository.clear();
      return null;
    }
    if (!forceRefresh && isAccessTokenFresh(session)) return session.accessToken;

    refreshInFlight ??= refreshToken(normalizedbaseUrl, session)
      .finally(() => { refreshInFlight = null; });
    return refreshInFlight;
  }

  return {
    async beginInteractiveSignIn(baseUrl) {
      const normalizedbaseUrl = normalizeBaseUrl(baseUrl);
      const redirectUri = authBrowser.redirectUrl('cantaro-auth');
      const state = createRandomBase64Url(24);
      const verifier = createRandomBase64Url(48);
      const authorizeUrl = new URL(`${normalizedbaseUrl}/api/auth/extension/authorize`);
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

      const grant = await postGrant(normalizedbaseUrl, new URLSearchParams({
        grant_type: 'authorization_code',
        code,
        client_id: authBrowser.runtimeId(),
        redirect_uri: redirectUri,
        code_verifier: verifier,
      }));
      return repository.save(toSession(normalizedbaseUrl, grant));
    },

    getAccessToken,

    async getVerifiedUser(baseUrl) {
      const normalizedbaseUrl = normalizeBaseUrl(baseUrl);
      const token = await getAccessToken(normalizedbaseUrl);
      if (!token) return null;
      const first = await verifyUser(normalizedbaseUrl, token);
      if (first.status === 'verified') {
        await saveVerifiedEmail(repository, first.user);
        return first.user;
      }
      if (first.status === 'unavailable') return null;

      const refreshedToken = await getAccessToken(normalizedbaseUrl, true);
      if (!refreshedToken) return null;
      const second = await verifyUser(normalizedbaseUrl, refreshedToken);
      if (second.status === 'verified') return second.user;
      if (second.status === 'rejected') {
        await clearRejectedAccessToken(repository, normalizedbaseUrl, refreshedToken);
      }
      return null;
    },

    async signOut(baseUrl) {
      const normalizedbaseUrl = normalizeBaseUrl(baseUrl);
      await refreshInFlight?.catch(() => null);
      const session = await repository.read();
      if (isSessionForApi(session, normalizedbaseUrl) && session.refreshToken) {
        await fetch(`${normalizedbaseUrl}/api/auth/extension/revoke`, {
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
