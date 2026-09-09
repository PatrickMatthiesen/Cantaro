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
function Harness({ url }: { url: string }) { current = useCollectionConsent(url, true); return null; }

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
