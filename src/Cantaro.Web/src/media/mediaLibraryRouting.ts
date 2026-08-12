import type { MediaLibraryPageDto, MediaProviderAccountStatusDto } from '@cantaro/client-shared/media';

export function shouldRedirectEmptyLibraryToProviders(
  library: Pick<MediaLibraryPageDto, 'totalCount'> | null,
  providerStatus: Pick<MediaProviderAccountStatusDto, 'isConnected'>,
): boolean {
  return !providerStatus.isConnected && (library === null || library.totalCount === 0);
}
