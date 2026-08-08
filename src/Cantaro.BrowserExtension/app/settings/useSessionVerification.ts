import { useEffect } from 'react';
import { verifyRuntimeSession } from '../../platform/auth/runtimeAuthClient';
import type { ExtensionSession } from '../../platform/auth/extensionSession';
import { browserSessionRepository } from '../../platform/auth/sessionRepository';
import type { ExtensionSettings } from '../../platform/settings/extensionSettings';

async function verifySession(settings: ExtensionSettings) {
  const user = await verifyRuntimeSession(settings.apiBaseUrl);
  return { user, session: await browserSessionRepository.read() };
}

export function useSessionVerification({ settings, session, onSession, onEmail, onChecking }: {
  settings: ExtensionSettings;
  session: ExtensionSession | null;
  onSession: (session: ExtensionSession | null) => void;
  onEmail: (email: string | null) => void;
  onChecking: (checking: boolean) => void;
}) {
  useEffect(() => {
    let active = true;
    if (!session) {
      onEmail(null);
      onChecking(false);
      return () => { active = false; };
    }

    onChecking(true);
    void verifySession(settings)
      .then((result) => {
        if (!active) return;
        onSession(result.session);
        onEmail(result.user?.email ?? null);
      })
      .catch(() => {
        if (!active) return;
        onSession(null);
        onEmail(null);
      })
      .finally(() => {
        if (active) onChecking(false);
      });
    return () => { active = false; };
  }, [onChecking, onEmail, onSession, session?.accessToken, session?.refreshToken, settings]);
}
