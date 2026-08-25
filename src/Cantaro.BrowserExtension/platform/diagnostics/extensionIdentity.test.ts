import { describe, expect, it } from 'vitest';
import {
  extensionBuildChannelForMode,
  extensionLogMessage,
  extensionNamespace,
  hashExtensionRuntimeId,
} from './extensionIdentity';

describe('extension identity', () => {
  it('maps WXT development mode to the dev channel', () => {
    expect(extensionBuildChannelForMode('development')).toBe('dev');
    expect(extensionBuildChannelForMode('production')).toBe('prod');
    expect(extensionBuildChannelForMode('staging')).toBe('prod');
  });

  it('creates a stable short namespace from the runtime ID and channel', () => {
    const devNamespace = extensionNamespace('abcdefghijklmnop', 'dev');
    expect(devNamespace).toBe(extensionNamespace('abcdefghijklmnop', 'dev'));
    expect(devNamespace).toMatch(/^cantaro-dev-[a-z0-9]{13}$/);
    expect(extensionNamespace('abcdefghijklmnop', 'dev')).not.toBe(
      extensionNamespace('abcdefghijklmnop', 'prod'),
    );
    expect(extensionNamespace('abcdefghijklmnop', 'dev')).not.toBe(
      extensionNamespace('ponmlkjihgfedcba', 'dev'),
    );
    expect(hashExtensionRuntimeId('')).toMatch(/^[a-z0-9]{13}$/);
  });

  it('labels development content-script logs without changing production wording', () => {
    expect(extensionLogMessage('Cantaro: Crunchyroll watch started', 'dev'))
      .toBe('Cantaro Dev: Crunchyroll watch started');
    expect(extensionLogMessage('Crunchyroll watch started', 'dev'))
      .toBe('Cantaro Dev: Crunchyroll watch started');
    expect(extensionLogMessage('Cantaro: Crunchyroll watch started', 'prod'))
      .toBe('Cantaro: Crunchyroll watch started');
  });
});
