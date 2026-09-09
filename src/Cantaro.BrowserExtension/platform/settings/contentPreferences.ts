import { isEpisodeTrackingPaused } from '../../features/media/settings/episodeTrackingPreference';
import { createCorrelationId, messageSuccess, type MessageResult } from '../messaging/messageResult';
import { browserSettingsRepository, SETTINGS_STORAGE_KEY } from './settingsRepository';

interface ContentPreferences {
  verboseLogging: boolean;
  trackingPaused: boolean;
}

interface ContentPreferencesRequest {
  type: 'content.preferences';
  correlationId: string;
}

export function isContentPreferencesRequest(value: unknown): value is ContentPreferencesRequest {
  return Boolean(value && typeof value === 'object'
    && (value as ContentPreferencesRequest).type === 'content.preferences'
    && typeof (value as ContentPreferencesRequest).correlationId === 'string');
}

async function currentPreferences(): Promise<ContentPreferences> {
  const [settings, trackingPaused] = await Promise.all([
    browserSettingsRepository.read(), isEpisodeTrackingPaused(),
  ]);
  return { verboseLogging: settings.verboseLogging, trackingPaused };
}

export async function handleContentPreferencesRequest(request: ContentPreferencesRequest) {
  return messageSuccess(await currentPreferences(), request.correlationId);
}

export async function readContentPreferences(): Promise<ContentPreferences> {
  const result = await browser.runtime.sendMessage({
    type: 'content.preferences', correlationId: createCorrelationId(),
  }) as MessageResult<ContentPreferences>;
  if (!result?.ok) throw new Error('Could not load content preferences.');
  return result.value;
}

export function watchContentPreferences(listener: (preferences: ContentPreferences) => void): () => void {
  let revision = 0;
  const onMessage = (message: unknown) => {
    if (message && typeof message === 'object' && 'type' in message && message.type === 'content.preferences.changed') {
      // Read fresh values so a delayed notification cannot restore an old preference.
      const requestRevision = ++revision;
      void readContentPreferences().then(preferences => {
        if (revision === requestRevision) listener(preferences);
      }).catch(() => {
        if (revision === requestRevision) listener({ verboseLogging: false, trackingPaused: true });
      });
    }
  };
  browser.runtime.onMessage.addListener(onMessage);
  return () => { revision++; browser.runtime.onMessage.removeListener(onMessage); };
}

export function initializeContentPreferences(): void {
  browser.storage.onChanged.addListener((changes, areaName) => {
    if (areaName !== 'local' || !(SETTINGS_STORAGE_KEY in changes || 'cantaro.media.trackingPause.v1' in changes)) return;
    void browser.tabs.query({ url: 'https://www.crunchyroll.com/*' }).then(tabs => Promise.all(
      tabs.flatMap(tab => tab.id === undefined ? [] : [
        browser.tabs.sendMessage(tab.id, { type: 'content.preferences.changed' }).catch(() => undefined),
      ]),
    )).catch(() => undefined);
  });
}
