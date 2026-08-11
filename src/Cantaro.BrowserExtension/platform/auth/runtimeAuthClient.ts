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
  getAccessToken(baseUrl: string, forceRefresh?: boolean): Promise<string | null>;
} = {
  async getAccessToken(baseUrl, forceRefresh = false) {
    const result = await sendAuthRequest<{ accessToken: string | null }>({
      type: 'auth.token.get',
      correlationId: createCorrelationId(),
      payload: { baseUrl, forceRefresh },
    });
    return result.accessToken;
  },
};

export async function verifyRuntimeSession(baseUrl: string): Promise<ExtensionUser | null> {
  const result = await sendAuthRequest<{ user: ExtensionUser | null }>({
    type: 'auth.session.verify',
    correlationId: createCorrelationId(),
    payload: { baseUrl },
  });
  return result.user;
}

export async function signOutRuntimeSession(baseUrl: string): Promise<void> {
  await sendAuthRequest<{ signedOut: true }>({
    type: 'auth.session.signOut',
    correlationId: createCorrelationId(),
    payload: { baseUrl },
  });
}
