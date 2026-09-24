import { describe, expect, it, vi } from 'vitest';
import { CONSENT_STORAGE_KEY, LEGACY_CONSENT_STORAGE_KEY, createConsentRepository, CURRENT_CONSENT_VERSION, type StoredConsent } from './consentRepository';
import { createConsentService } from './consentService';

function storage(initial: unknown = undefined, initialKey = CONSENT_STORAGE_KEY, additional: Record<string, unknown> = {}) {
  const values = new Map<string, unknown>([[initialKey, initial]]);
  for (const [key, value] of Object.entries(additional)) values.set(key, value);
  return {
    get: vi.fn(async (keys: string | string[]) => Object.fromEntries(
      (Array.isArray(keys) ? keys : [keys]).filter(key => values.has(key)).map(key => [key, values.get(key)]),
    )),
    set: vi.fn(async (items: Record<string, unknown>) => {
      for (const [key, value] of Object.entries(items)) values.set(key, value);
    }),
    remove: vi.fn(async (key: string) => { values.delete(key); }),
  };
}

function consentService(user: { email: string } | null, initial?: unknown) {
  const area = storage(initial);
  const repository = createConsentRepository(area);
  return { area, service: createConsentService(repository, { getVerifiedUser: vi.fn(async () => user) }) };
}

describe('consentService', () => {
  it.each([false, true])('preserves version 3 choices after the title-hints update (lyrics=%s)', async musicLyrics => {
    const { service, area } = consentService({ email: 'user@example.test' }, {
      version: 3, baseUrl: 'https://api.example.test', userEmail: 'user@example.test',
      consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: false, musicLyrics,
    });
    await expect(service.getStatus('https://api.example.test')).resolves.toMatchObject({
      needsReview: false, watchTrackingAllowed: true, catalogCollectionAllowed: false, musicLyricsAllowed: musicLyrics,
    });
    expect(area.set).not.toHaveBeenCalled();
    await expect(service.getStatus('https://other.example.test')).resolves.toMatchObject({ needsReview: true, musicLyricsAllowed: false });
  });

  it('defaults to denied and requires first-use consent', async () => {
    const { service } = consentService({ email: 'user@example.test' });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toEqual({
        consentVersion: CURRENT_CONSENT_VERSION,
        needsReview: true,
        authenticated: true,
        watchTrackingAllowed: false,
        catalogCollectionAllowed: false,
        musicLyricsAllowed: false,
      });
  });

  it('remembers an explicit denied choice while signed out', async () => {
    const { service } = consentService(null);
    await expect(service.save('https://api.example.test', {
      watchTracking: false, catalogCollection: false, musicLyrics: false,
    })).resolves.toMatchObject({ needsReview: false, authenticated: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: false, authenticated: false, watchTrackingAllowed: false });
  });

  it('binds enabled choices to the authenticated identity and API origin', async () => {
    const { service } = consentService({ email: 'User@Example.test' });
    await service.save('https://api.example.test/', { watchTracking: true, catalogCollection: false, musicLyrics: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: false, watchTrackingAllowed: true, catalogCollectionAllowed: false });
  });

  it('does not reuse enabled consent for another signed-in identity or origin', async () => {
    const record: StoredConsent = {
      version: CURRENT_CONSENT_VERSION,
      baseUrl: 'https://api.example.test', userEmail: 'first@example.test',
      consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: true, musicLyrics: false,
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
      consentedAt: new Date().toISOString(), watchTracking: false, catalogCollection: true, musicLyrics: false,
    };
    const { service } = consentService(null, record);
    await expect(service.save('https://api.example.test', { watchTracking: true, catalogCollection: false, musicLyrics: false }))
      .rejects.toThrow('Sign in to Cantaro');
  });

  it('can disable one purpose locally without waiting for account verification', async () => {
    const record: StoredConsent = {
      version: CURRENT_CONSENT_VERSION,
      baseUrl: 'https://api.example.test', userEmail: 'user@example.test',
      consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: true, musicLyrics: false,
    };
    const { service } = consentService(null, record);
    await expect(service.save('https://api.example.test', { watchTracking: false, catalogCollection: true, musicLyrics: false }))
      .resolves.toMatchObject({ authenticated: false, watchTrackingAllowed: false, catalogCollectionAllowed: false });
  });

  it('requires authentication before enabling collection', async () => {
    const { service } = consentService(null);
    await expect(service.save('https://api.example.test', { watchTracking: true, catalogCollection: false, musicLyrics: false }))
      .rejects.toThrow('Sign in to Cantaro');
  });

  it('requires sign-in before enabling YouTube lyrics and keeps the choice account-bound', async () => {
    const { service } = consentService(null);
    await expect(service.save('https://api.example.test', {
      watchTracking: false, catalogCollection: false, musicLyrics: true,
    })).rejects.toThrow('Sign in to Cantaro');

    const signedIn = consentService({ email: 'user@example.test' });
    await signedIn.service.save('https://api.example.test', {
      watchTracking: false, catalogCollection: false, musicLyrics: true,
    });
    await expect(signedIn.service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: false, musicLyricsAllowed: true });
    await signedIn.service.save('https://api.example.test', {
      watchTracking: false, catalogCollection: false, musicLyrics: false,
    });
    await expect(signedIn.service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ musicLyricsAllowed: false });
  });

  it.each([1, 2])('preserves legacy media choices from consent version %s while lyrics stay denied', async version => {
    const area = storage({
      version, baseUrl: 'https://api.example.test', userEmail: 'user@example.test',
      consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: true,
    }, LEGACY_CONSENT_STORAGE_KEY);
    const service = createConsentService(createConsentRepository(area), {
      getVerifiedUser: vi.fn(async () => ({ email: 'user@example.test' })),
    });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: false, watchTrackingAllowed: true, catalogCollectionAllowed: true, musicLyricsAllowed: false });
    expect(area.set).not.toHaveBeenCalled();
  });

  it('removes the legacy consent key after saving a migrated choice', async () => {
    const area = storage({
      version: 2, baseUrl: 'https://api.example.test', userEmail: 'user@example.test',
      consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: false,
    }, LEGACY_CONSENT_STORAGE_KEY);
    const service = createConsentService(createConsentRepository(area), {
      getVerifiedUser: vi.fn(async () => ({ email: 'user@example.test' })),
    });
    await service.save('https://api.example.test', { watchTracking: true, catalogCollection: false, musicLyrics: false });
    expect(area.remove).toHaveBeenCalledWith(LEGACY_CONSENT_STORAGE_KEY);
  });

  it('does not fall back to a legacy grant when the current consent key is malformed', async () => {
    const area = storage({
      version: 4, baseUrl: 'https://api.example.test', userEmail: 'user@example.test',
      consentedAt: new Date().toISOString(), watchTracking: false, catalogCollection: false, musicLyrics: 'invalid',
    }, CONSENT_STORAGE_KEY, {
      [LEGACY_CONSENT_STORAGE_KEY]: {
        version: 2, baseUrl: 'https://api.example.test', userEmail: 'user@example.test',
        consentedAt: new Date().toISOString(), watchTracking: true, catalogCollection: true,
      },
    });
    const service = createConsentService(createConsentRepository(area), {
      getVerifiedUser: vi.fn(async () => ({ email: 'user@example.test' })),
    });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ needsReview: true, watchTrackingAllowed: false, catalogCollectionAllowed: false, musicLyricsAllowed: false });
  });

  it('clears both consent keys on revocation', async () => {
    const { area, service } = consentService({ email: 'user@example.test' });
    await service.revoke();
    expect(area.remove).toHaveBeenCalledWith(CONSENT_STORAGE_KEY);
    expect(area.remove).toHaveBeenCalledWith(LEGACY_CONSENT_STORAGE_KEY);
  });

  it('clears consent on revocation', async () => {
    const { area, service } = consentService({ email: 'user@example.test' });
    await service.save('https://api.example.test', { watchTracking: true, catalogCollection: true, musicLyrics: false });
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
    const saving = service.save('https://api.example.test', { watchTracking: true, catalogCollection: false, musicLyrics: false });
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
    await service.save('https://api.example.test', { watchTracking: true, catalogCollection: false, musicLyrics: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ authenticated: true, watchTrackingAllowed: true, catalogCollectionAllowed: false });
    await service.save('https://api.example.test', { watchTracking: true, catalogCollection: false, musicLyrics: false });
    await expect(service.getStatus('https://api.example.test'))
      .resolves.toMatchObject({ watchTrackingAllowed: true, catalogCollectionAllowed: false });
  });

  it('does not expose the old grant while a revoke write is still pending', async () => {
    let releaseClear: (() => void) | undefined;
    const area = storage();
    area.remove.mockImplementation(key => key === CONSENT_STORAGE_KEY
      ? new Promise<void>(resolve => { releaseClear = resolve; })
      : Promise.resolve());
    // Use a service with deferred storage so write ordering is observable.
    const deferred = createConsentService(createConsentRepository(area), {
      getVerifiedUser: vi.fn(async () => ({ email: 'user@example.test' })),
    });
    await deferred.save('https://api.example.test', { watchTracking: true, catalogCollection: false, musicLyrics: false });
    const revoking = deferred.revoke();
    const status = deferred.getStatus('https://api.example.test');
    await vi.waitFor(() => expect(releaseClear).toBeDefined());
    releaseClear?.();
    await revoking;
    await expect(status).resolves.toMatchObject({ watchTrackingAllowed: false, needsReview: true });
  });
});
