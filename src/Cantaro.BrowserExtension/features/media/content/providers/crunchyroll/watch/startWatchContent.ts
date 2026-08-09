import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import { isEpisodeTrackingPaused } from '../../../../settings/episodeTrackingPreference';
import { browserSettingsRepository } from '../../../../../../platform/settings/settingsRepository';
import type { MediaBackgroundRequest } from '../../../../contracts/mediaMessages';
import type {
  ResolveWatchObservationRequest,
  WatchProgressObservation,
  WatchSubmissionResult,
} from '../../../../contracts/watchObservation';
import type { MessageResult } from '../../../../../../platform/messaging/messageResult';
import { watchVerboseLoggingSetting } from '../../../runtime/contentControllerRuntime';
import { createCrunchyrollWatchController } from './watchController';
import { showWatchResolutionOverlay } from './watchResolutionOverlay';
import { watchTrackingPauseChanges } from './watchPauseChanges';

export function startCrunchyrollWatchContent(ctx: ContentScriptContext): void {
  createCrunchyrollWatchController(ctx, {
    isTrackingPaused: isEpisodeTrackingPaused,
    watchTrackingPause: watchTrackingPauseChanges,
    submitWatch,
    resolveWatch,
    showResolution: showWatchResolutionOverlay,
    readVerboseLogging: async () => (await browserSettingsRepository.read()).verboseLogging,
    watchVerboseLogging: watchVerboseLoggingSetting,
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
