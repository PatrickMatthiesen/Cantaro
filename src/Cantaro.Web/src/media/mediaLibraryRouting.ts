import type { MediaLibraryPageDto, MediaProviderAccountStatusDto } from '@cantaro/client-shared/media';
import { connectedMediaProviderIds } from '@cantaro/client-shared/media';

export function shouldRedirectEmptyLibraryToProviders(
  library: Pick<MediaLibraryPageDto, 'totalCount'> | null,
  providerStatus: Pick<MediaProviderAccountStatusDto, 'isConnected'> | ReadonlyArray<Pick<MediaProviderAccountStatusDto, 'providerId' | 'isConnected'>>,
): boolean {
  const isConnected = Array.isArray(providerStatus)
    ? connectedMediaProviderIds(providerStatus).length > 0
    : (providerStatus as Pick<MediaProviderAccountStatusDto, 'isConnected'>).isConnected;
  return !isConnected && (library === null || library.totalCount === 0);
}
