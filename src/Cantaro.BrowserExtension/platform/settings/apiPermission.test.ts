import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  apiOriginMatchPattern,
  ensureApiPermission,
  removeReplacedApiPermission,
} from './apiPermission';
import { DEFAULT_BASE_URL } from './extensionSettings';

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

  it('rejects an untrusted HTTP origin without requesting it', async () => {
    const request = vi.fn();
    vi.stubGlobal('browser', {
      permissions: {
        contains: vi.fn(async () => false),
        request,
      },
    });

    await expect(ensureApiPermission('http://cantaro.example.test'))
      .rejects.toThrow('must use HTTPS');
    expect(request).not.toHaveBeenCalled();
  });

  it('removes the exact optional origin when an instance is replaced', async () => {
    const remove = vi.fn(async () => true);
    vi.stubGlobal('browser', { permissions: { remove } });

    await removeReplacedApiPermission(
      'https://old.example.test/path',
      'https://new.example.test',
    );

    expect(remove).toHaveBeenCalledWith({ origins: ['https://old.example.test/*'] });
  });

  it('does not try to remove the build-time origin', async () => {
    const remove = vi.fn();
    vi.stubGlobal('browser', { permissions: { remove } });

    await removeReplacedApiPermission(DEFAULT_BASE_URL, 'https://new.example.test');

    expect(remove).not.toHaveBeenCalled();
  });

  it('retains permission when only the path changes on the same origin', async () => {
    const remove = vi.fn();
    vi.stubGlobal('browser', { permissions: { remove } });

    await removeReplacedApiPermission(
      'https://cantaro.example.test/old',
      'https://cantaro.example.test/new',
    );

    expect(remove).not.toHaveBeenCalled();
  });
});
