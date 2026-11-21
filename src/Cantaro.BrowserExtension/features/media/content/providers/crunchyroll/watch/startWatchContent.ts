import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import {
  readContentPreferences,
  watchContentPreferences,
} from '../../../../../../platform/settings/contentPreferences';
import { contentCollectionConsent } from '../../../../../../platform/consent/runtimeConsentClient';
import type { MediaBackgroundRequest } from '../../../../contracts/mediaMessages';
import type {
  ResolveWatchObservationRequest,
  WatchProgressObservation,
  WatchSubmissionResult,
} from '../../../../contracts/watchObservation';
import type { MessageResult } from '../../../../../../platform/messaging/messageResult';
import { createCrunchyrollWatchController } from './watchController';
import { showWatchResolutionOverlay } from './watchResolutionOverlay';

export function startCrunchyrollWatchContent(ctx: ContentScriptContext): void {
  const watchConsent = contentCollectionConsent('watch');
  const catalogConsent = contentCollectionConsent('catalog');
  createCrunchyrollWatchController(ctx, {
    isTrackingPaused: async () => (await readContentPreferences()).trackingPaused,
    watchTrackingPause: listener => watchContentPreferences(() => listener()),
    submitWatch,
    resolveWatch,
    showResolution: showWatchResolutionOverlay,
    readVerboseLogging: async () => (await readContentPreferences()).verboseLogging,
    watchVerboseLogging: listener => watchContentPreferences(preferences => listener(preferences.verboseLogging)),
    readCollectionConsent: watchConsent.read,
    watchCollectionConsent: watchConsent.watch,
    readCatalogCollectionConsent: catalogConsent.read,
    watchCatalogCollectionConsent: catalogConsent.watch,
  });
}

function submitWatch(
  observation: WatchProgressObservation,
  correlationId: string,
): Promise<MessageResult<WatchSubmissionResult>> {
  return browser.runtime.sendMessage({
    type: 'media.watch.submit',
    payload: observation,
    correlationId,
  } satisfies MediaBackgroundRequest) as Promise<MessageResult<WatchSubmissionResult>>;
}

function resolveWatch(
  request: ResolveWatchObservationRequest,
  correlationId: string,
): Promise<MessageResult<{ resolved: true }>> {
  return browser.runtime.sendMessage({
    type: 'media.watch.resolve',
    payload: request,
    correlationId,
  } satisfies MediaBackgroundRequest) as Promise<MessageResult<{ resolved: true }>>;
}
