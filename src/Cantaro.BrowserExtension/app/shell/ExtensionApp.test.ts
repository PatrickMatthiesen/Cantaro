import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { parseHTML } from 'linkedom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ExtensionApp } from './ExtensionApp';

const mocks = vi.hoisted(() => ({
  configured: false,
  signIn: vi.fn(),
  getRuntimeConsentStatus: vi.fn(),
  saveRuntimeConsent: vi.fn(),
  revokeRuntimeConsent: vi.fn(),
  subscribeRuntimeConsent: vi.fn(() => () => {}),
}));
vi.mock('../settings/useSettings', () => ({ useSettings: () => ({
  savedSettings: { baseUrl: 'https://cantaro.example' },
  configured: mocks.configured, loading: false, isSigningIn: false,
  signIn: mocks.signIn,
}) }));
vi.mock('../../platform/consent/runtimeConsentClient', () => mocks);
vi.mock('./browserPopupServices', () => ({ browserPopupServices: {} }));
vi.mock('./usePopupNavigation', () => ({ usePopupNavigation: () => ({ section: 'media' }) }));
vi.mock('./useActiveTabContext', () => ({ useActiveTabContext: () => ({ state: {} }) }));
vi.mock('./usePopupNotice', () => ({ usePopupNotice: () => ({ notice: null, showNotice: vi.fn() }) }));
vi.mock('./PopupHeader', () => ({ PopupHeader: () => null }));
vi.mock('./TabContextSummary', () => ({ TabContextSummary: () => null }));
vi.mock('./StatusToast', () => ({ StatusToast: () => null }));
vi.mock('../settings/SettingsPage', () => ({ SettingsPage: () => null }));
vi.mock('../music/MusicPage', () => ({ MusicPage: () => null }));
vi.mock('../media/MediaPage', () => ({ MediaPage: () => 'Media ready' }));

const initialStatus = {
  consentVersion: 1, needsReview: true, authenticated: false,
  watchTrackingAllowed: false, catalogCollectionAllowed: false,
};
let root: Root;
let container: HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  mocks.configured = false;
  mocks.getRuntimeConsentStatus.mockResolvedValue(initialStatus);
  const dom = parseHTML('<html><body><div id="root"></div></body></html>');
  vi.stubGlobal('window', dom.window);
  vi.stubGlobal('document', dom.document);
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = dom.document.getElementById('root') as unknown as HTMLElement;
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  vi.unstubAllGlobals();
});

async function clickButton(label: string) {
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === label);
  expect(button).toBeDefined();
  await act(async () => button!.click());
}

describe('extension consent sign-in', () => {
  it.each([false, true])('preserves the draft across authentication and handles save failure=%s', async saveFails => {
    let finishSignIn: (success: boolean) => void = () => {};
    mocks.signIn.mockImplementation(() => new Promise<boolean>(resolve => { finishSignIn = resolve; }));
    if (saveFails) mocks.saveRuntimeConsent.mockRejectedValue(new Error('Storage unavailable'));
    else mocks.saveRuntimeConsent.mockResolvedValue({
      ...initialStatus, authenticated: true, needsReview: false, watchTrackingAllowed: true,
    });
    await act(async () => root.render(createElement(ExtensionApp)));
    const checkbox = container.querySelector('input')!;
    await act(async () => {
      checkbox.checked = true;
      checkbox.dispatchEvent(new window.Event('click', { bubbles: true }));
    });
    await clickButton('Sign in to enable collection');
    expect(mocks.saveRuntimeConsent).not.toHaveBeenCalled();

    mocks.configured = true;
    mocks.getRuntimeConsentStatus.mockResolvedValue({ ...initialStatus, authenticated: true });
    await act(async () => root.render(createElement(ExtensionApp)));
    expect(Array.from(container.querySelectorAll('input')).map(input => input.checked)).toEqual([true, false]);

    await act(async () => finishSignIn(true));
    expect(mocks.saveRuntimeConsent).toHaveBeenCalledExactlyOnceWith('https://cantaro.example', {
      watchTracking: true, catalogCollection: false,
    });
    if (saveFails) {
      expect(container.querySelector('[role="alert"]')?.textContent).toContain('Could not save collection choices');
      expect(Array.from(container.querySelectorAll('input')).map(input => input.checked)).toEqual([true, false]);
      expect(container.textContent).toContain('Allow selected collection');
    } else {
      expect(container.textContent).toContain('Media ready');
    }
  });
});
