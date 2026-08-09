import type {
  AuthBackgroundRequest,
  AuthBackgroundResponse,
} from '../messaging/authMessages';
import { createCorrelationId, type MessageResult } from '../messaging/messageResult';
import type { ExtensionUser } from './extensionSession';

async function sendAuthRequest<T extends AuthBackgroundResponse>(
  request: AuthBackgroundRequest,
): Promise<T> {
  const result = await browser.runtime.sendMessage(request) as MessageResult<T>;
  if (!result?.ok) {
    throw new Error(result?.error.message ?? 'The extension background service did not respond.');
  }
  return result.value;
}

export const runtimeAccessTokenProvider: {
  getAccessToken(apiBaseUrl: string, forceRefresh?: boolean): Promise<string | null>;
} = {
  async getAccessToken(apiBaseUrl, forceRefresh = false) {
    const result = await sendAuthRequest<{ accessToken: string | null }>({
      type: 'auth.token.get',
      correlationId: createCorrelationId(),
      payload: { apiBaseUrl, forceRefresh },
    });
    return result.accessToken;
  },
};

export async function verifyRuntimeSession(apiBaseUrl: string): Promise<ExtensionUser | null> {
  const result = await sendAuthRequest<{ user: ExtensionUser | null }>({
    type: 'auth.session.verify',
    correlationId: createCorrelationId(),
    payload: { apiBaseUrl },
  });
  return result.user;
}

export async function signOutRuntimeSession(apiBaseUrl: string): Promise<void> {
  await sendAuthRequest<{ signedOut: true }>({
    type: 'auth.session.signOut',
    correlationId: createCorrelationId(),
    payload: { apiBaseUrl },
  });
}
