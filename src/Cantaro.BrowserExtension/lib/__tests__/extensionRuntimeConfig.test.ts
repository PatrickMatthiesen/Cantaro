import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  apiBaseUrlOriginMatchPattern,
  emptyExtensionConfig,
  readExtensionConfig,
  saveExtensionConfig,
} from '../extensionRuntimeConfig';

describe('apiBaseUrlOriginMatchPattern', () => {
  it('maps an API base URL to a host permission match pattern', () => {
    expect(apiBaseUrlOriginMatchPattern('https://localhost:7203/api')).toBe('https://localhost:7203/*');
  });

  it('returns null for invalid URLs', () => {
    expect(apiBaseUrlOriginMatchPattern('not a url')).toBeNull();
  });

  it('defaults verbose logging to off', async () => {
    vi.stubGlobal('browser', {
      storage: { local: { get: vi.fn().mockResolvedValue({}) } },
    });

    expect((await readExtensionConfig()).verboseLogging).toBe(false);
  });

  it('persists the verbose logging preference', async () => {
    const set = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('browser', { storage: { local: { set } } });

    const persisted = await saveExtensionConfig({
      ...emptyExtensionConfig,
      verboseLogging: true,
    });

    expect(persisted.verboseLogging).toBe(true);
    expect(set).toHaveBeenCalledWith(expect.objectContaining({ verboseLogging: true }));
  });

  afterEach(() => vi.unstubAllGlobals());
});
