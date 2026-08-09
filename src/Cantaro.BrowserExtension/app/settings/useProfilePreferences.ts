import { useEffect } from 'react';
import { runtimeAccessTokenProvider } from '../../platform/auth/runtimeAuthClient';
import type { ExtensionSettings } from '../../platform/settings/extensionSettings';

type ProfilePreferencesResponse = {
  preferences?: { theme?: 'system' | 'light' | 'dark'; blurEmailAddress?: boolean };
};

function resolveTheme(profile: ProfilePreferencesResponse): 'light' | 'dark' {
  const preference = profile.preferences?.theme ?? 'system';
  if (preference !== 'system') return preference;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

async function loadProfilePreferences(settings: ExtensionSettings) {
  const accessToken = await runtimeAccessTokenProvider.getAccessToken(settings.apiBaseUrl);
  if (!accessToken) throw new Error('No active extension session.');
  const response = await fetch(`${settings.apiBaseUrl}/api/profile`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (!response.ok) throw new Error('Could not load profile preferences.');
  const profile = await response.json() as ProfilePreferencesResponse;
  return {
    blurEmailAddress: profile.preferences?.blurEmailAddress ?? true,
    theme: resolveTheme(profile),
  };
}

async function applyProfilePreferences(preferences: { blurEmailAddress: boolean; theme: 'light' | 'dark' }) {
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

export function useProfilePreferences(settings: ExtensionSettings, configured: boolean, onBlurPreference: (blur: boolean) => void) {
  useEffect(() => {
    if (!configured) return;
    let active = true;

    void loadProfilePreferences(settings)
      .then((preferences) => {
        if (!active) return;
        onBlurPreference(preferences.blurEmailAddress);
        void applyProfilePreferences(preferences);
      })
      .catch(() => {
        void readStoredBlurPreference().then((blur) => {
          if (active) onBlurPreference(blur);
        });
      });

    return () => { active = false; };
  }, [configured, onBlurPreference, settings]);
}
