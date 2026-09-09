import { isMediaBackgroundRequest } from '../../features/media/contracts/mediaMessages';
import { mediaRequestHandler, type MediaRequestHandler } from '../../features/media/background/mediaRequestHandler';
import { browserAuthService, type AuthService } from '../auth/authService';
import { browserSessionRepository } from '../auth/sessionRepository';
import { browserConsentService, type ConsentService } from '../consent/consentService';
import { CURRENT_CONSENT_VERSION } from '../consent/consentRepository';
import { createExtensionLogger } from '../diagnostics/logger';
import { isAuthBackgroundRequest, type AuthBackgroundRequest } from '../messaging/authMessages';
import { isConsentBackgroundRequest, type ConsentBackgroundRequest } from '../messaging/consentMessages';
import { messageFailure, messageSuccess } from '../messaging/messageResult';
import { normalizeBaseUrl } from '../settings/extensionSettings';
import { browserSettingsRepository, type SettingsRepository } from '../settings/settingsRepository';
import { handleContentPreferencesRequest, isContentPreferencesRequest } from '../settings/contentPreferences';

const logger = createExtensionLogger({ scope: 'background' });

export interface RuntimeRouterDependencies {
  authService: Pick<AuthService, 'getAccessToken' | 'getVerifiedUser' | 'signOut'>;
  consentService: Pick<ConsentService, 'getStatus' | 'save' | 'revoke'>;
  settingsRepository: SettingsRepository;
}

const browserRouterDependencies: RuntimeRouterDependencies = {
  authService: browserAuthService,
  consentService: browserConsentService,
  settingsRepository: browserSettingsRepository,
};

export function consentDenyStatus(authenticated: boolean) {
  return {
    consentVersion: CURRENT_CONSENT_VERSION,
    needsReview: true,
    authenticated,
    watchTrackingAllowed: false,
    catalogCollectionAllowed: false,
  };
}

export function broadcastConsentChanged(status: object): void {
  const message = { type: 'consent.changed', payload: status };
  // Runtime messages reach extension pages such as the popup. Content scripts
  // are addressed through their tabs because runtime broadcast excludes them.
  void browser.runtime.sendMessage(message).catch(() => undefined);
  if (!browser.tabs?.query || !browser.tabs.sendMessage) return;
  void browser.tabs.query({}).then(tabs => Promise.all(
    tabs.flatMap(tab => tab.id === undefined ? [] : [
      browser.tabs.sendMessage(tab.id, message).catch(() => undefined),
    ]),
  )).catch(() => undefined);
}

function refreshConsentStatus(
  baseUrl: string,
  dependencies: RuntimeRouterDependencies,
): void {
  void dependencies.consentService.getStatus(baseUrl)
    .then(next => broadcastConsentChanged(next))
    .catch(() => undefined);
}

async function revokeConsentAndSignOut(
  baseUrl: string,
  dependencies: RuntimeRouterDependencies,
): Promise<void> {
  broadcastConsentChanged(consentDenyStatus(false));
  let revokeError: unknown;
  try {
    await dependencies.consentService.revoke();
  } catch (error) {
    revokeError = error;
    logger.warn('Could not persist consent revocation during sign-out', {
      reason: error instanceof Error ? error.message : 'unknown',
    });
  }
  let signOutError: unknown;
  try {
    await dependencies.authService.signOut(baseUrl);
  } catch (error) {
    signOutError = error;
  }
  if (signOutError) throw signOutError;
  if (revokeError) throw revokeError;
}

async function handleConsentStatus(
  baseUrl: string,
  correlationId: string,
  dependencies: RuntimeRouterDependencies,
) {
  const status = await dependencies.consentService.getStatus(baseUrl);
  return messageSuccess(status, correlationId);
}

async function handleConsentRevoke(
  baseUrl: string,
  correlationId: string,
  dependencies: RuntimeRouterDependencies,
) {
  const status = consentDenyStatus(false);
  broadcastConsentChanged(status);
  let revokeError: unknown;
  try {
    await dependencies.consentService.revoke();
  } catch (error) {
    revokeError = error;
  }
  if (revokeError) throw revokeError;
  refreshConsentStatus(baseUrl, dependencies);
  return messageSuccess(status, correlationId);
}

async function handleConsentSave(
  baseUrl: string,
  correlationId: string,
  choices: Extract<ConsentBackgroundRequest, { type: 'consent.save' }>['payload'],
  dependencies: RuntimeRouterDependencies,
) {
  broadcastConsentChanged(consentDenyStatus(false));
  let status;
  try {
    status = await dependencies.consentService.save(baseUrl, choices);
  } catch (error) {
    // A failed write leaves the consent service latched closed. Notify tabs
    // immediately so an old allow cannot keep a content controller active.
    broadcastConsentChanged(consentDenyStatus(false));
    throw error;
  }
  broadcastConsentChanged(status);
  refreshConsentStatus(baseUrl, dependencies);
  return messageSuccess(status, correlationId);
}

async function handleAuthRequest(
  request: AuthBackgroundRequest,
  sender: Browser.runtime.MessageSender,
  dependencies: RuntimeRouterDependencies,
) {
  try {
    // Access tokens are only ever returned to extension pages. Content scripts
    // can ask for the redacted session/consent status instead.
    if (sender.tab) {
      return messageFailure(request.correlationId, 'invalid_request', 'Authentication controls are unavailable to website content scripts.');
    }
    const settings = await dependencies.settingsRepository.read();
    if (normalizeBaseUrl(request.payload.baseUrl) !== settings.baseUrl) {
      return messageFailure(
        request.correlationId,
        'invalid_request',
        'The requested API origin does not match the configured Cantaro origin.',
      );
    }
    if (request.type === 'auth.session.signOut') {
      await revokeConsentAndSignOut(settings.baseUrl, dependencies);
      return messageSuccess({ signedOut: true }, request.correlationId);
    }
    const value = request.type === 'auth.token.get'
      ? {
        accessToken: await dependencies.authService.getAccessToken(
          settings.baseUrl,
          request.payload.forceRefresh,
        )
      }
      : { user: await dependencies.authService.getVerifiedUser(settings.baseUrl) };
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

async function handleConsentRequest(
  request: ConsentBackgroundRequest,
  sender: Browser.runtime.MessageSender,
  dependencies: RuntimeRouterDependencies,
) {
  try {
    const settings = await dependencies.settingsRepository.read();
    if (request.payload.baseUrl !== undefined
      && normalizeBaseUrl(request.payload.baseUrl) !== settings.baseUrl) {
      return messageFailure(request.correlationId, 'invalid_request', 'The requested API origin does not match the configured Cantaro origin.');
    }
    // Content scripts may read the effective, redacted status, but only the
    // trusted extension UI may change consent.
    if (sender.tab && request.type !== 'consent.status') {
      return messageFailure(request.correlationId, 'invalid_request', 'Only extension pages may change collection consent.');
    }
    if (request.type === 'consent.status') {
      return await handleConsentStatus(settings.baseUrl, request.correlationId, dependencies);
    }
    if (request.type === 'consent.revoke') {
      return await handleConsentRevoke(settings.baseUrl, request.correlationId, dependencies);
    }
    return await handleConsentSave(settings.baseUrl, request.correlationId, request.payload, dependencies);
  } catch (error) {
    logger.error('Consent request failed', error);
    return messageFailure(request.correlationId, 'unexpected', error instanceof Error ? error.message : 'Consent request failed.', true);
  }
}

export function createRuntimeRouter(
  mediaHandler: MediaRequestHandler,
  dependencies: RuntimeRouterDependencies = browserRouterDependencies,
) {
  return async (message: unknown, sender: Browser.runtime.MessageSender): Promise<unknown> => {
    const tabId = sender.tab?.id;
    if (isContentPreferencesRequest(message)) return handleContentPreferencesRequest(message);
    if (isAuthBackgroundRequest(message)) return handleAuthRequest(message, sender, dependencies);
    if (isConsentBackgroundRequest(message)) return handleConsentRequest(message, sender, dependencies);
    if (isMediaBackgroundRequest(message)) return mediaHandler.handle(message, tabId);
    return undefined;
  };
}

export const runtimeRouter = createRuntimeRouter(mediaRequestHandler);
