import { describe, expect, it, vi } from 'vitest';
import { CONSENT_STORAGE_KEY, createConsentRepository, CURRENT_CONSENT_VERSION, type StoredConsent } from './consentRepository';
import { createConsentService } from './consentService';

function storage(initial: unknown = undefined) {
  let value = initial;
  return {
    get: vi.fn(async () => ({ 'cantaro.consent.v1': value })),
    set: vi.fn(async (items: Record<string, unknown>) => { value = items['cantaro.consent.v1']; }),
    remove: vi.fn(async () => { value = undefined; }),
  };
}

function consentService(user: { email: string } | null, initial?: unknown) {
  const area = storage(initial);
  const repository = createConsentRepository(area);
  return { area, service: createConsentService(repository, { getVerifiedUser: vi.fn(async () => user) }) };
}

describe('consentService', () => {
  it('defaults to denied and requires first-use consent', async () => {
    const { service } = consentService({ email: 'user@example.test' });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toEqual({
        consentVersion: CURRENT_CONSENT_VERSION,
        needsReview: true,
        authenticated: true,
        watchTrackingAllowed: false,
        catalogCollectionAllowed: false,
      });
  });

  it('remembers an explicit denied choice while signed out', async () => {
    const { service } = consentService(null);
    await expect(service.save('https://api.example.test', {
      watchTracking: false, catalogCollection: false,
    })).resolves.toMatchObject({ needsReview: false, authenticated: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: false, authenticated: false, watchTrackingAllowed: false });
  });

  it('binds enabled choices to the authenticated identity and API origin', async () => {
    const { service } = consentService({ email: 'User@Example.test' });
    await service.save('https://api.example.test/', { watchTracking: true, catalogCollection: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: false, watchTrackingAllowed: true, catalogCollectionAllowed: false });
  });

  it('does not reuse enabled consent for another signed-in identity or origin', async () => {
    const record: StoredConsent = {
      version: CURRENT_CONSENT_VERSION,
      baseUrl: 'https://api.example.test', userEmail: 'first@example.test',
      consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: true,
    };
    const { service } = consentService({ email: 'second@example.test' }, record);
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: true, authenticated: true, watchTrackingAllowed: false });
    await expect(service.getStatus('https://other.example.test'))
      .resolves.toMatchObject({ needsReview: true, watchTrackingAllowed: false });
  });

  it('does not enable a new purpose from stale stored consent without verification', async () => {
    const record: StoredConsent = {
      version: CURRENT_CONSENT_VERSION,
      baseUrl: 'https://api.example.test', userEmail: 'first@example.test',
      consentedAt: new Date().toISOString(), watchTracking: false, catalogCollection: true,
    };
    const { service } = consentService(null, record);
    await expect(service.save('https://api.example.test', { watchTracking: true, catalogCollection: false }))
      .rejects.toThrow('Sign in to Cantaro');
  });

  it('can disable one purpose locally without waiting for account verification', async () => {
    const record: StoredConsent = {
      version: CURRENT_CONSENT_VERSION,
      baseUrl: 'https://api.example.test', userEmail: 'user@example.test',
      consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: true,
    };
    const { service } = consentService(null, record);
    await expect(service.save('https://api.example.test', { watchTracking: false, catalogCollection: true }))
      .resolves.toMatchObject({ authenticated: false, watchTrackingAllowed: false, catalogCollectionAllowed: false });
  });

  it('requires authentication before enabling collection', async () => {
    const { service } = consentService(null);
    await expect(service.save('https://api.example.test', { watchTracking: true, catalogCollection: false }))
      .rejects.toThrow('Sign in to Cantaro');
  });

  it('clears consent on revocation', async () => {
    const { area, service } = consentService({ email: 'user@example.test' });
    await service.save('https://api.example.test', { watchTracking: true, catalogCollection: true });
    await service.revoke();
    expect(area.remove).toHaveBeenCalledWith(CONSENT_STORAGE_KEY);
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: true, watchTrackingAllowed: false, catalogCollectionAllowed: false });
  });

  it('cannot restore consent after revocation wins an in-flight save', async () => {
    const area = storage();
    let resolveUser: ((user: { email: string }) => void) | undefined;
    const repository = createConsentRepository(area);
    const service = createConsentService(repository, {
      getVerifiedUser: () => new Promise(resolve => { resolveUser = resolve; }),
    });
    const saving = service.save('https://api.example.test', { watchTracking: true, catalogCollection: false });
    await service.revoke();
    resolveUser?.({ email: 'user@example.test' });
    await expect(saving).rejects.toThrow('consent changed');
    await expect(repository.read()).resolves.toBeNull();
  });

  it('requires fresh opt-in after identity revocation and preserves a remaining purpose', async () => {
    const { service } = consentService({ email: 'user@example.test' });
    await service.revoke();
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ authenticated: true, needsReview: true });
    await service.save('https://api.example.test', { watchTracking: true, catalogCollection: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ authenticated: true, watchTrackingAllowed: true, catalogCollectionAllowed: false });
    await service.save('https://api.example.test', { watchTracking: true, catalogCollection: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ watchTrackingAllowed: true, catalogCollectionAllowed: false });
  });

  it('does not expose the old grant while a revoke write is still pending', async () => {
    let releaseClear: (() => void) | undefined;
    const area = storage();
    area.remove.mockImplementation(() => new Promise<void>(resolve => { releaseClear = resolve; }));
    // Use a service with deferred storage so write ordering is observable.
    const deferred = createConsentService(createConsentRepository(area), {
      getVerifiedUser: vi.fn(async () => ({ email: 'user@example.test' })),
    });
    await deferred.save('https://api.example.test', { watchTracking: true, catalogCollection: false });
    const revoking = deferred.revoke();
    const status = deferred.getStatus('https://api.example.test');
    await Promise.resolve();
    releaseClear?.();
    await revoking;
    await expect(status).resolves.toMatchObject({ watchTrackingAllowed: false, needsReview: true });
  });
});
