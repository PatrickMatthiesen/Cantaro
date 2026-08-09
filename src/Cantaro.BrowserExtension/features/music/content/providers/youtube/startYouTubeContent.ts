import type { ContentScriptContext } from 'wxt/utils/content-script-context';
import {
  browserSettingsRepository,
  watchExtensionSettings,
} from '../../../../../platform/settings/settingsRepository';
import { createYouTubeMusicController } from './youtubeController';

export function startYouTubeMusicContent(ctx: ContentScriptContext): void {
  createYouTubeMusicController(ctx, {
    readSettings: async () => {
      const [settings, themePreference] = await Promise.all([
        browserSettingsRepository.read(),
        browser.storage.local.get('cantaroTheme'),
      ]);
      return {
        injectLyrics: settings.injectLyricsOnYouTube,
        theme: themePreference.cantaroTheme === 'light' || themePreference.cantaroTheme === 'dark'
          ? themePreference.cantaroTheme
          : undefined,
      };
    },
    watchSettings: (listener) => {
      const stopWatchingSettings = watchExtensionSettings(listener);
      const onChanged = (
        changes: Record<string, { newValue?: unknown; oldValue?: unknown }>,
        areaName: string,
      ) => {
        if (areaName === 'local' && changes.cantaroTheme) listener();
      };
      browser.storage.onChanged.addListener(onChanged);
      return () => {
        stopWatchingSettings();
        browser.storage.onChanged.removeListener(onChanged);
      };
    },
  });
}
