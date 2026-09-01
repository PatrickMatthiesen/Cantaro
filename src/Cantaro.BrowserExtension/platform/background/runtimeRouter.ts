import { isMediaBackgroundRequest } from '../../features/media/contracts/mediaMessages';
import { mediaRequestHandler, type MediaRequestHandler } from '../../features/media/background/mediaRequestHandler';
import { browserAuthService } from '../auth/authService';
import { createExtensionLogger } from '../diagnostics/logger';
import { isAuthBackgroundRequest, type AuthBackgroundRequest } from '../messaging/authMessages';
import { messageFailure, messageSuccess } from '../messaging/messageResult';
import { normalizeBaseUrl } from '../settings/extensionSettings';
import { browserSettingsRepository } from '../settings/settingsRepository';

const logger = createExtensionLogger({ scope: 'background' });

async function handleAuthRequest(request: AuthBackgroundRequest) {
  try {
    const settings = await browserSettingsRepository.read();
    if (normalizeBaseUrl(request.payload.baseUrl) !== settings.baseUrl) {
      return messageFailure(
        request.correlationId,
        'invalid_request',
        'The requested API origin does not match the configured Cantaro origin.',
      );
    }
    if (request.type === 'auth.session.signOut') {
      await browserAuthService.signOut(settings.baseUrl);
      return messageSuccess({ signedOut: true }, request.correlationId);
    }
    const value = request.type === 'auth.token.get'
      ? {
        accessToken: await browserAuthService.getAccessToken(
          settings.baseUrl,
          request.payload.forceRefresh,
        )
      }
      : { user: await browserAuthService.getVerifiedUser(settings.baseUrl) };
    return messageSuccess(value, request.correlationId);
  } catch (error) {
    logger.error('Authentication request failed', error);
    return messageFailure(
      request.correlationId,
      'unexpected',
      error instanceof Error ? error.message : 'Authentication request failed.',
      true,
    );
  }
}

export function createRuntimeRouter(mediaHandler: MediaRequestHandler) {
  return async (message: unknown, sender: Browser.runtime.MessageSender): Promise<unknown> => {
    const tabId = sender.tab?.id;
    if (isAuthBackgroundRequest(message)) return handleAuthRequest(message);
    if (isMediaBackgroundRequest(message)) return mediaHandler.handle(message, tabId);
    return undefined;
  };
}

export const runtimeRouter = createRuntimeRouter(mediaRequestHandler);
