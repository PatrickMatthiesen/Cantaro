import { describe, expect, test } from 'bun:test';
import {
  connectedMediaProviderIds,
  mediaProviderCatalog,
  mediaProviderIconId,
} from '../../Cantaro.ClientShared/src/media/services/mediaProviders';
import { mediaKindLabel } from '../../Cantaro.ClientShared/src/media/services/mediaFormatting';

describe('media provider catalog', () => {
  test('includes SIMKL as an implemented TV and movie provider', () => {
    expect(mediaProviderCatalog.find((provider) => provider.id === 'simkl')).toMatchObject({
      name: 'SIMKL',
      iconId: 'simkl',
      implemented: true,
    });
    expect(mediaProviderIconId('simkl')).toBe('simkl');
    expect(mediaProviderIconId('SIMKL')).toBe('simkl');
  });

  test('selects every connected provider for shared imports', () => {
    expect(connectedMediaProviderIds([
      { providerId: 'anilist', isConnected: false },
      { providerId: 'simkl', isConnected: true },
      { providerId: 'myanimelist', isConnected: true },
    ])).toEqual(['simkl', 'myanimelist']);
  });

  test('uses TV and movie labels for SIMKL media kinds', () => {
    expect(mediaKindLabel('series')).toBe('TV Series');
    expect(mediaKindLabel('movie')).toBe('Movie');
  });
});
