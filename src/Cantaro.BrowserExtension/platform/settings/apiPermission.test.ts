import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiOriginMatchPattern, ensureApiPermission } from './apiPermission';

describe('apiPermission', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('normalizes a configured API URL to its origin pattern', () => {
    expect(apiOriginMatchPattern('https://api.example.test/path/'))
      .toBe('https://api.example.test/*');
  });

  it('does not prompt when the exact origin is already allowed', async () => {
    const request = vi.fn();
    vi.stubGlobal('browser', {
      permissions: {
        contains: vi.fn(async () => true),
        request,
      },
    });

    await ensureApiPermission('https://api.example.test');
    expect(request).not.toHaveBeenCalled();
  });

  it('reports a denied origin request', async () => {
    vi.stubGlobal('browser', {
      permissions: {
        contains: vi.fn(async () => false),
        request: vi.fn(async () => false),
      },
    });

    await expect(ensureApiPermission('https://api.example.test'))
      .rejects.toThrow('was not granted');
  });
});
