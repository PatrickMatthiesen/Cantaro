import { useEffect } from 'react';
import { runtimeAccessTokenProvider } from '../../platform/auth/runtimeAuthClient';
import type { ExtensionSettings } from '../../platform/settings/extensionSettings';
import { applyPopupTheme } from './popupTheme';

type ProfilePreferencesResponse = {
  preferences?: {
    theme?: 'system' | 'light' | 'dark';
    blurEmailAddress?: boolean;
    disabledWatchProviders?: string[];
  };
};

type ExtensionProfilePreferences = {
  blurEmailAddress: boolean;
  disabledWatchProviders: string[];
  theme: 'system' | 'light' | 'dark';
};

const defaultProfilePreferences = {
  blurEmailAddress: true,
  disabledWatchProviders: [] as string[],
  theme: 'system' as const,
};

async function loadProfilePreferences(settings: ExtensionSettings) {
  const accessToken = await runtimeAccessTokenProvider.getAccessToken(settings.baseUrl);
  if (!accessToken) throw new Error('No active extension session.');
  const response = await fetch(`${settings.baseUrl}/api/profile`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (!response.ok) throw new Error('Could not load profile preferences.');
  const profile = await response.json() as ProfilePreferencesResponse;
  const preferences = { ...defaultProfilePreferences, ...profile.preferences };
  return {
    blurEmailAddress: preferences.blurEmailAddress,
    disabledWatchProviders: preferences.disabledWatchProviders,
    theme: preferences.theme,
  };
}

async function readStoredBlurPreference() {
  const stored: Record<string, unknown> = await browser.storage.local.get('blurEmailAddress').catch(() => ({}));
  return typeof stored.blurEmailAddress === 'boolean' ? stored.blurEmailAddress : true;
}

export function useProfilePreferences(settings: ExtensionSettings, configured: boolean, onPreferences: (preferences: Omit<ExtensionProfilePreferences, 'theme'>) => void) {
  useEffect(() => {
    if (!configured) return;
    let active = true;

    void loadProfilePreferences(settings)
      .then((preferences) => {
        if (!active) return;
        onPreferences(preferences);
        applyPopupTheme(preferences.theme);
        void browser.storage.local.set({
          blurEmailAddress: preferences.blurEmailAddress,
          cantaroTheme: preferences.theme,
        }).catch(() => undefined);
      })
      .catch(() => {
        void readStoredBlurPreference().then((blur) => {
          if (active) onPreferences({ blurEmailAddress: blur, disabledWatchProviders: [] });
        });
      });

    return () => { active = false; };
  }, [configured, onPreferences, settings]);
}
