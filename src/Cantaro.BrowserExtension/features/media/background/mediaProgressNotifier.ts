import type { WatchSubmissionResult } from '../contracts/watchObservation';
import { browserSettingsRepository, type SettingsRepository } from '../../../platform/settings/settingsRepository';

const MEDIA_PROGRESS_UPDATED_EVENT = 'cantaro:media-progress-updated';

interface MediaProgressUpdateNotification {
  mediaTitleId: string;
  progressEpisodes?: number;
}

export interface MediaProgressNotifier {
  notify(result: WatchSubmissionResult): Promise<void>;
}

function cantaroTabPattern(baseUrl: string): string | null {
  try {
    return `${new URL(baseUrl).origin}/*`;
  } catch {
    return null;
  }
}

function createMediaProgressNotifier(
  settingsRepository: SettingsRepository,
): MediaProgressNotifier {
  return {
    async notify(result) {
      if (!result.progressUpdated || !result.matchedMediaTitleId) {
        return;
      }

      const settings = await settingsRepository.read();
      const pattern = cantaroTabPattern(settings.baseUrl);
      if (!pattern) {
        return;
      }

      const tabs = await browser.tabs.query({ url: pattern });
      const payload: MediaProgressUpdateNotification = {
        mediaTitleId: result.matchedMediaTitleId,
        progressEpisodes: result.resolvedProgress,
      };
      await Promise.allSettled(tabs.flatMap((tab) => tab.id === undefined ? [] : [
        browser.scripting.executeScript({
          target: { tabId: tab.id },
          func: (eventName: string, serializedPayload: string) => {
            window.dispatchEvent(new CustomEvent(eventName, { detail: serializedPayload }));
          },
          args: [MEDIA_PROGRESS_UPDATED_EVENT, JSON.stringify(payload)],
        }),
      ]));
    },
  };
}

export const browserMediaProgressNotifier = createMediaProgressNotifier(browserSettingsRepository);
