import { useEffect } from 'react';
import { runtimeAccessTokenProvider } from '../../platform/auth/runtimeAuthClient';
import type { ExtensionSettings } from '../../platform/settings/extensionSettings';

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
  theme: 'light' | 'dark';
};

const defaultProfilePreferences = {
  blurEmailAddress: true,
  disabledWatchProviders: [] as string[],
  theme: 'system' as const,
};

function resolveTheme(preference: 'system' | 'light' | 'dark'): 'light' | 'dark' {
  if (preference !== 'system') return preference;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

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
    theme: resolveTheme(preferences.theme),
  };
}

async function applyProfilePreferences(preferences: ExtensionProfilePreferences) {
  await browser.storage.local.set({
    blurEmailAddress: preferences.blurEmailAddress,
    cantaroTheme: preferences.theme,
  });
  document.documentElement.dataset.theme = preferences.theme;
}

async function readStoredBlurPreference() {
  const stored = await browser.storage.local.get('blurEmailAddress');
  return typeof stored.blurEmailAddress === 'boolean' ? stored.blurEmailAddress : true;
}

export function useProfilePreferences(settings: ExtensionSettings, configured: boolean, onPreferences: (preferences: ExtensionProfilePreferences) => void) {
  useEffect(() => {
    if (!configured) return;
    let active = true;

    void loadProfilePreferences(settings)
      .then((preferences) => {
        if (!active) return;
        onPreferences(preferences);
        void applyProfilePreferences(preferences);
      })
      .catch(() => {
        void readStoredBlurPreference().then((blur) => {
          if (active) onPreferences({ blurEmailAddress: blur, disabledWatchProviders: [], theme: 'light' });
        });
      });

    return () => { active = false; };
  }, [configured, onPreferences, settings]);
}
