import { useCallback, useState } from 'react';
import { browserAuthService } from '../../platform/auth/authService';
import type { ExtensionSession } from '../../platform/auth/extensionSession';
import { signOutRuntimeSession } from '../../platform/auth/runtimeAuthClient';
import { createCorrelationId } from '../../platform/messaging/messageResult';
import {
  ensureApiPermission,
  removeReplacedApiPermission,
} from '../../platform/settings/apiPermission';
import {
  DEFAULT_BASE_URL,
  normalizeBaseUrl,
  type ExtensionSettings,
} from '../../platform/settings/extensionSettings';
import { browserSettingsRepository } from '../../platform/settings/settingsRepository';
import { createSettingsUpdate, type SettingsDraft } from './settingsModel';

type SettingsNotice = (message: string, tone: 'success' | 'error') => void;

interface SettingsActionContext {
  savedSettings: ExtensionSettings;
  draft: SettingsDraft;
  notify: SettingsNotice;
  applySettings: (settings: ExtensionSettings) => void;
  setSession: (session: ExtensionSession | null) => void;
  setSessionEmail: (email: string | null) => void;
}

async function removePreviousPermissionIfChanged(
  previousBaseUrl: string,
  currentBaseUrl: string,
  changed: boolean,
): Promise<void> {
  if (!changed) return;
  await removeReplacedApiPermission(previousBaseUrl, currentBaseUrl);
}

export function useSettingsActions(context: SettingsActionContext) {
  const [isSigningIn, setIsSigningIn] = useState(false);
  const [isDisconnecting, setIsDisconnecting] = useState(false);

  const persist = useCallback(async (settings: ExtensionSettings, message: string) => {
    context.applySettings(await browserSettingsRepository.save(settings));
    context.notify(message, 'success');
  }, [context.applySettings, context.notify]);

  const save = useCallback(async () => {
    try {
      const update = createSettingsUpdate(context.savedSettings, context.draft);
      await ensureApiPermission(update.settings.baseUrl);
      if (update.baseUrlChanged) {
        await signOutRuntimeSession(context.savedSettings.baseUrl);
        context.setSession(null);
        context.setSessionEmail(null);
      }
      await persist(update.settings, update.baseUrlChanged
        ? 'API origin updated. Stored Cantaro session was cleared.'
        : 'Extension settings saved');
      await removePreviousPermissionIfChanged(
        context.savedSettings.baseUrl,
        update.settings.baseUrl,
        update.baseUrlChanged,
      );
      return true;
    } catch (error) {
      console.error('Error saving settings:', error);
      context.notify('Failed to save extension settings', 'error');
      return false;
    }
  }, [context, persist]);

  const signIn = useCallback(async () => {
    const update = createSettingsUpdate(context.savedSettings, context.draft);
    const baseUrl = normalizeBaseUrl(update.settings.baseUrl) || DEFAULT_BASE_URL;
    setIsSigningIn(true);
    try {
      await ensureApiPermission(baseUrl);
      if (update.baseUrlChanged) {
        await signOutRuntimeSession(context.savedSettings.baseUrl);
        context.setSession(null);
        context.setSessionEmail(null);
      }
      await persist(update.settings, 'Extension settings saved');
      await removePreviousPermissionIfChanged(
        context.savedSettings.baseUrl,
        update.settings.baseUrl,
        update.baseUrlChanged,
      );
      const session = await browserAuthService.beginInteractiveSignIn(baseUrl);
      context.setSession(session);
      context.setSessionEmail(session.email || null);
      context.notify('Signed in to Cantaro', 'success');
      await browser.runtime.sendMessage({
        type: 'delivery.queue.drain',
        correlationId: createCorrelationId(),
      }).catch(() => null);
      return true;
    } catch (error) {
      console.error('Extension sign-in failed:', error);
      context.notify(error instanceof Error ? error.message : 'Sign-in failed.', 'error');
      return false;
    } finally {
      setIsSigningIn(false);
    }
  }, [context, persist]);

  const disconnect = useCallback(async () => {
    const baseUrl = normalizeBaseUrl(context.savedSettings.baseUrl) || DEFAULT_BASE_URL;
    setIsDisconnecting(true);
    try {
      await signOutRuntimeSession(baseUrl);
      context.setSession(null);
      context.setSessionEmail(null);
      context.notify('Cantaro session cleared', 'success');
      return true;
    } catch (error) {
      console.error('Extension sign-out failed:', error);
      context.notify('Failed to clear the stored Cantaro session.', 'error');
      return false;
    } finally {
      setIsDisconnecting(false);
    }
  }, [context]);

  return { isSigningIn, isDisconnecting, save, signIn, disconnect };
}
