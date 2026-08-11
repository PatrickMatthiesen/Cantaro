import { afterEach, describe, expect, it, vi } from 'vitest';
import packageMetadata from '../../package.json';
import { getExtensionVersion } from './extensionVersion';

describe('getExtensionVersion', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('reads the installed extension manifest at runtime', () => {
    vi.stubGlobal('browser', {
      runtime: {
        getManifest: () => ({ version: '2.3.4' }),
      },
    });

    expect(getExtensionVersion()).toBe('2.3.4');
  });

  it('uses package metadata outside a browser runtime', () => {
    vi.stubGlobal('browser', undefined);

    expect(getExtensionVersion()).toBe(packageMetadata.version);
  });
});
