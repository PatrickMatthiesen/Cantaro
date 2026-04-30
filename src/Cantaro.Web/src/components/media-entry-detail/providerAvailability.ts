import type { MediaProviderAvailabilityLinkDto } from '../../services/mediaApi';

export interface ProviderAvailabilityState {
  status: 'loading' | 'loaded' | 'error';
  links: MediaProviderAvailabilityLinkDto[];
  error?: string;
}

export type ProviderAvailabilityMap = Record<string, ProviderAvailabilityState>;

export function providerAvailabilityKey(provider: string, externalId: string): string {
  return `${provider}:${externalId}`;
}
