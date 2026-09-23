import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { parseHTML } from 'linkedom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConsentStatus } from '../../platform/consent/consentService';
import { useCollectionConsent } from './useCollectionConsent';

const runtime = vi.hoisted(() => ({
  getRuntimeConsentStatus: vi.fn(), saveRuntimeConsent: vi.fn(), revokeRuntimeConsent: vi.fn(),
  subscribeRuntimeConsent: vi.fn(),
}));
vi.mock('../../platform/consent/runtimeConsentClient', () => runtime);

const denied: ConsentStatus = { consentVersion: 1, needsReview: true, authenticated: true,
  watchTrackingAllowed: false, catalogCollectionAllowed: false };
const allowed = { ...denied, needsReview: false, watchTrackingAllowed: true };
let root: Root;
let current: ReturnType<typeof useCollectionConsent>;
let notify: (status: ConsentStatus) => void;
function Harness({ url, configured = true }: { url: string; configured?: boolean }) {
  current = useCollectionConsent(url, configured);
  return null;
}

beforeEach(() => {
  vi.resetAllMocks();
  const dom = parseHTML('<html><body><div id="root"></div></body></html>');
  vi.stubGlobal('window', dom.window);
  vi.stubGlobal('document', dom.document);
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  root = createRoot(dom.document.getElementById('root') as unknown as HTMLElement);
  runtime.getRuntimeConsentStatus.mockResolvedValue(denied);
  runtime.subscribeRuntimeConsent.mockImplementation(listener => { notify = listener; return () => {}; });
});
afterEach(async () => { await act(async () => root.unmount()); vi.unstubAllGlobals(); });

describe('collection consent popup state', () => {
  it('keeps the consent form mounted while refreshing status after sign-in', async () => {
    const url = 'https://api.example';
    runtime.getRuntimeConsentStatus.mockResolvedValue({ ...denied, authenticated: false });
    await act(async () => root.render(createElement(Harness, { url, configured: false })));
    let complete: (status: ConsentStatus) => void = () => {};
    runtime.getRuntimeConsentStatus.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    await act(async () => root.render(createElement(Harness, { url, configured: true })));
    expect(current.loading).toBe(false);
    expect(current.status).not.toBeNull();
    await act(async () => complete(denied));
    expect(current.status?.authenticated).toBe(true);
  });

  it('does not save choices from an old server after sign-in finishes on another server', async () => {
    await act(async () => root.render(createElement(Harness, { url: 'https://first.example' })));
    const saveForOriginalServer = current.save;
    await act(async () => root.render(createElement(Harness, { url: 'https://second.example' })));
    await act(async () => saveForOriginalServer({ watchTracking: true, catalogCollection: true }));
    expect(runtime.saveRuntimeConsent).not.toHaveBeenCalled();
  });

  it('reports a failed save even if authentication refreshes consent while it is pending', async () => {
    let rejectSave: (reason: Error) => void = () => {};
    runtime.saveRuntimeConsent.mockImplementation(() => new Promise((_, reject) => { rejectSave = reject; }));
    await act(async () => root.render(createElement(Harness, { url: 'https://api.example' })));
    let pending: Promise<void>;
    await act(async () => { pending = current.save({ watchTracking: true, catalogCollection: false }); });
    await act(async () => notify(denied));
    expect(current.busy).toBe(true);
    await act(async () => { rejectSave(new Error('Storage unavailable')); await pending; });
    expect(current.error).toContain('Could not save collection choices');
    expect(current.busy).toBe(false);
    expect(current.status?.watchTrackingAllowed).toBe(false);
  });

  it('ignores an old server save after the configured server changes', async () => {
    let complete: (status: ConsentStatus) => void = () => {};
    runtime.saveRuntimeConsent.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    await act(async () => root.render(createElement(Harness, { url: 'https://first.example' })));
    let pending: Promise<void>;
    await act(async () => { pending = current.save({ watchTracking: true, catalogCollection: false }); });
    await act(async () => root.render(createElement(Harness, { url: 'https://second.example' })));
    await act(async () => { complete(allowed); await pending; });
    expect(current.status?.watchTrackingAllowed).toBe(false);
  });

  it('does not restore allowed state from a save response after a revocation notification', async () => {
    let complete: (status: ConsentStatus) => void = () => {};
    runtime.saveRuntimeConsent.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    await act(async () => root.render(createElement(Harness, { url: 'https://api.example' })));
    let pending: Promise<void>;
    await act(async () => { pending = current.save({ watchTracking: true, catalogCollection: false }); });
    await act(async () => notify(denied));
    await act(async () => { complete(allowed); await pending; });
    expect(current.status?.watchTrackingAllowed).toBe(false);
  });
});
