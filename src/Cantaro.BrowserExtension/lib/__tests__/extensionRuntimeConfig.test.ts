import { describe, expect, it } from 'vitest';
import { apiBaseUrlOriginMatchPattern } from '../extensionRuntimeConfig';

describe('apiBaseUrlOriginMatchPattern', () => {
  it('maps an API base URL to a host permission match pattern', () => {
    expect(apiBaseUrlOriginMatchPattern('https://localhost:7203/api')).toBe('https://localhost:7203/*');
  });

  it('returns null for invalid URLs', () => {
    expect(apiBaseUrlOriginMatchPattern('not a url')).toBeNull();
  });
});