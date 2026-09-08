import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import {
  readContentPreferences,
  watchContentPreferences,
} from '../../../../../../platform/settings/contentPreferences';
import {
  readContentConsentStatus,
  watchContentConsentStatus,
} from '../../../../../../platform/consent/runtimeConsentClient';
import type {
  CatalogSubmissionResult,
  SeriesCatalogObservation,
} from '../../../../contracts/catalogObservation';
import type { MediaBackgroundRequest } from '../../../../contracts/mediaMessages';
import type { MessageResult } from '../../../../../../platform/messaging/messageResult';
import { createCrunchyrollSeriesController } from './seriesController';

export function startCrunchyrollSeriesContent(ctx: ContentScriptContext): void {
  createCrunchyrollSeriesController(ctx, {
    submitCatalog: (observation, correlationId) => submitCatalog(observation, correlationId),
    readVerboseLogging: async () => (await readContentPreferences()).verboseLogging,
    watchVerboseLogging: listener => watchContentPreferences(preferences => listener(preferences.verboseLogging)),
    readCollectionConsent: async () => {
      const status = await readContentConsentStatus();
      return status.authenticated && status.catalog;
    },
    watchCollectionConsent: listener => watchContentConsentStatus(status => {
      listener(status.authenticated && status.catalog);
    }),
  });
}

async function submitCatalog(
  observation: SeriesCatalogObservation,
  correlationId: string,
): Promise<MessageResult<CatalogSubmissionResult>> {
  return browser.runtime.sendMessage({
    type: 'media.catalog.submit',
    payload: observation,
    correlationId,
  } satisfies MediaBackgroundRequest) as Promise<MessageResult<CatalogSubmissionResult>>;
}
