import { useCallback, useEffect, useState } from 'react';
import type { ExtensionSession } from '../../platform/auth/extensionSession';
import { browserSessionRepository } from '../../platform/auth/sessionRepository';
import {
  defaultExtensionSettings,
  type ExtensionSettings,
} from '../../platform/settings/extensionSettings';
import { browserSettingsRepository } from '../../platform/settings/settingsRepository';
import { draftFromSettings, type SettingsDraft } from './settingsModel';
import { sanitizeDiagnosticDetails } from '../../platform/diagnostics/logger';

type SettingsNotice = (message: string, tone: 'success' | 'error') => void;

export function useSettingsState(notify: SettingsNotice) {
  const [savedSettings, setSavedSettings] = useState<ExtensionSettings>(defaultExtensionSettings);
  const [draft, setDraft] = useState<SettingsDraft>(() => draftFromSettings(defaultExtensionSettings));
  const [session, setSession] = useState<ExtensionSession | null>(null);
  const [sessionEmail, setSessionEmail] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const applySettings = useCallback((settings: ExtensionSettings) => {
    setSavedSettings(settings);
    setDraft(draftFromSettings(settings));
  }, []);

  const updateDraft = useCallback(<K extends keyof SettingsDraft>(key: K, value: SettingsDraft[K]) => {
    setDraft((current) => ({ ...current, [key]: value }));
  }, []);

  useEffect(() => {
    let active = true;
    void Promise.all([browserSettingsRepository.read(), browserSessionRepository.read()])
      .then(([settings, storedSession]) => {
        if (!active) return;
        applySettings(settings);
        setSession(storedSession);
        setSessionEmail(storedSession?.email || null);
      })
      .catch((error) => {
        console.error('Error loading settings:', sanitizeDiagnosticDetails(error));
        notify('Failed to load extension settings', 'error');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [applySettings, notify]);

  return {
    savedSettings,
    draft,
    session,
    sessionEmail,
    loading,
    applySettings,
    updateDraft,
    setSession,
    setSessionEmail,
  };
}
