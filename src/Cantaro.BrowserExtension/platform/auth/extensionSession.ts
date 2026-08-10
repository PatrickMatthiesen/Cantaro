export interface ExtensionSession {
  baseUrl: string;
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  email: string;
}
export interface ExtensionUser {
  email: string;
}

export function isAccessTokenFresh(session: ExtensionSession, now = Date.now()): boolean {
  if (!session.accessToken || !session.accessTokenExpiresAt) return false;
  const expiresAt = Date.parse(session.accessTokenExpiresAt);
  return !Number.isNaN(expiresAt) && expiresAt - now > 60_000;
}
