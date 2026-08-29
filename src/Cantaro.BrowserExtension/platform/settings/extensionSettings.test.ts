import { describe, expect, it } from 'vitest';
import { defaultVerboseLoggingForMode } from './extensionSettings';

describe('extension settings defaults', () => {
  it('enables verbose logging only for development builds', () => {
    expect(defaultVerboseLoggingForMode('development')).toBe(true);
    expect(defaultVerboseLoggingForMode('production')).toBe(false);
    expect(defaultVerboseLoggingForMode('test')).toBe(false);
  });
});
