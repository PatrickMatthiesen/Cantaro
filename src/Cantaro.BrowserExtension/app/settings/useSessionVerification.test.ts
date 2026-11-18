import { act, createElement, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { parseHTML } from 'linkedom';
import { expect, it, vi } from 'vitest';
import type { ExtensionSession } from '../../platform/auth/extensionSession';
import { defaultExtensionSettings } from '../../platform/settings/extensionSettings';
import { useSessionVerification } from './useSessionVerification';

const runtime = vi.hoisted(() => ({ verifyRuntimeSession: vi.fn(), readSession: vi.fn() }));
vi.mock('../../platform/auth/runtimeAuthClient', () => runtime);
vi.mock('../../platform/auth/sessionRepository', () => ({
  browserSessionRepository: { read: runtime.readSession },
}));

const savedSession: ExtensionSession = {
  baseUrl: defaultExtensionSettings.baseUrl,
  email: 'listener@example.test',
  accessToken: 'access',
  refreshToken: 'refresh',
  accessTokenExpiresAt: '',
};

function Harness() {
  const [session, setSession] = useState<ExtensionSession | null>(savedSession);
  const [email, setEmail] = useState<string | null>(savedSession.email);
  const [checking, setChecking] = useState(false);
  useSessionVerification({
    settings: defaultExtensionSettings, session, onSession: setSession,
    onEmail: setEmail, onChecking: setChecking,
  });
  return checking ? 'Checking' : session ? email : 'Signed out';
}

it.each([
  { scenario: 'background unavailable', throws: true, stored: savedSession, expected: savedSession.email },
  { scenario: 'server unavailable', throws: false, stored: savedSession, expected: savedSession.email },
  { scenario: 'session revoked', throws: false, stored: null, expected: 'Signed out' },
])('shows $expected when the $scenario', async ({ throws, stored, expected }) => {
  if (throws) runtime.verifyRuntimeSession.mockRejectedValue(new Error('Background unavailable'));
  else runtime.verifyRuntimeSession.mockResolvedValue(null);
  runtime.readSession.mockResolvedValue(stored);
  const { document, window } = parseHTML('<html><body><div id="root"></div></body></html>');
  vi.stubGlobal('window', window);
  vi.stubGlobal('document', document);
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const container = document.getElementById('root') as unknown as HTMLElement;
  const root = createRoot(container);
  try {
    await act(async () => root.render(createElement(Harness)));
    expect(container.textContent).toBe(expected);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
  }
});
