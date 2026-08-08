import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import { browserSettingsRepository } from '../../../../../../platform/settings/settingsRepository';
import type {
  CatalogSubmissionResult,
  SeriesCatalogObservation,
} from '../../../../contracts/catalogObservation';
import type { MediaBackgroundRequest } from '../../../../contracts/mediaMessages';
import type { MessageResult } from '../../../../../../platform/messaging/messageResult';
import { watchVerboseLoggingSetting } from '../../../runtime/contentControllerRuntime';
import { createCrunchyrollSeriesController } from './seriesController';

export function startCrunchyrollSeriesContent(ctx: ContentScriptContext): void {
  createCrunchyrollSeriesController(ctx, {
    submitCatalog: (observation, correlationId) => submitCatalog(observation, correlationId),
    readVerboseLogging: async () => (await browserSettingsRepository.read()).verboseLogging,
    watchVerboseLogging: watchVerboseLoggingSetting,
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
