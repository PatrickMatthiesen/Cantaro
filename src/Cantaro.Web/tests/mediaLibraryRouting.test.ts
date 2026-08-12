import { describe, expect, it } from 'vitest';
import { shouldRedirectEmptyLibraryToProviders } from '../src/media/mediaLibraryRouting';

describe('media library routing', () => {
  it('redirects a user with neither local viewer state nor a connected provider', () => {
    expect(shouldRedirectEmptyLibraryToProviders(
      { totalCount: 0 },
      { isConnected: false },
    )).toBe(true);
  });

  it('keeps an empty library accessible while a provider is connected', () => {
    expect(shouldRedirectEmptyLibraryToProviders(
      { totalCount: 0 },
      { isConnected: true },
    )).toBe(false);
  });

  it('keeps local viewer state accessible without a connected provider', () => {
    expect(shouldRedirectEmptyLibraryToProviders(
      { totalCount: 1 },
      { isConnected: false },
    )).toBe(false);
  });

  it('redirects to setup when the empty-state query fails for a disconnected user', () => {
    expect(shouldRedirectEmptyLibraryToProviders(
      null,
      { isConnected: false },
    )).toBe(true);
  });
});
