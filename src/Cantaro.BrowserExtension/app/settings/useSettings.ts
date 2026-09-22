import { useCallback, useState } from 'react';
import type { ExtensionSettings } from '../../platform/settings/extensionSettings';
import { hasSettingsChanges, type SettingsDraft } from './settingsModel';
import { useMediaApiConfiguration } from './useMediaApiConfiguration';
import { useProfilePreferences } from './useProfilePreferences';
import { useSessionVerification } from './useSessionVerification';
import { useSettingsActions } from './useSettingsActions';
import { useSettingsState } from './useSettingsState';

type SettingsNotice = (message: string, tone: 'success' | 'error') => void;

export interface SettingsController {
  savedSettings: ExtensionSettings;
  draft: SettingsDraft;
  sessionEmail: string | null;
  blurEmailAddress: boolean;
  disabledWatchProviders?: string[];
  loading: boolean;
  isSigningIn: boolean;
  isDisconnecting: boolean;
  isCheckingSession: boolean;
  configured: boolean;
  hasUnsavedChanges: boolean;
  updateDraft: <K extends keyof SettingsDraft>(key: K, value: SettingsDraft[K]) => void;
  save: () => Promise<boolean>;
  signIn: () => Promise<boolean>;
  disconnect: () => Promise<boolean>;
}

type ResolvedSettingsController = SettingsController & {
  disabledWatchProviders: string[];
};

export function useSettings(notify: SettingsNotice): ResolvedSettingsController {
  const state = useSettingsState(notify);
  const [blurEmailAddress, setBlurEmailAddress] = useState(true);
  const [disabledWatchProviders, setDisabledWatchProviders] = useState<string[]>([]);
  const updateProfilePreferences = useCallback((preferences: {
    blurEmailAddress: boolean;
    disabledWatchProviders: string[];
  }) => {
    setBlurEmailAddress(preferences.blurEmailAddress);
    setDisabledWatchProviders(preferences.disabledWatchProviders);
  }, []);
  const [isCheckingSession, setIsCheckingSession] = useState(false);
  const actions = useSettingsActions({
    savedSettings: state.savedSettings,
    draft: state.draft,
    notify,
    applySettings: state.applySettings,
    setSession: state.setSession,
    setSessionEmail: state.setSessionEmail,
  });

  useMediaApiConfiguration(state.savedSettings.baseUrl);
  useSessionVerification({
    settings: state.savedSettings,
    session: state.session,
    onSession: state.setSession,
    onEmail: state.setSessionEmail,
    onChecking: setIsCheckingSession,
  });
  useProfilePreferences(state.savedSettings, Boolean(state.session), updateProfilePreferences);

  return {
    savedSettings: state.savedSettings,
    draft: state.draft,
    sessionEmail: state.sessionEmail,
    blurEmailAddress,
    disabledWatchProviders,
    loading: state.loading,
    isSigningIn: actions.isSigningIn,
    isDisconnecting: actions.isDisconnecting,
    isCheckingSession,
    configured: Boolean(state.session),
    hasUnsavedChanges: hasSettingsChanges(state.savedSettings, state.draft),
    updateDraft: state.updateDraft,
    save: actions.save,
    signIn: actions.signIn,
    disconnect: actions.disconnect,
  };
}
