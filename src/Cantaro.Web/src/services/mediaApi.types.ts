// DTOs mirroring the backend contracts in Cantaro.Api.Models

export interface MediaLibraryPageDto {
    items: MediaLibraryListItemDto[];
    availableListNames: string[];
    totalCount: number;
    page: number;
    pageSize: number;
    totalPages: number;
}

export interface MediaLibraryListItemDto {
    id: string;
    mediaTitleId: string;
    canonicalTitle: string;
    originalTitle?: string;
    posterUrl?: string;
    mediaKind: string;
    normalizedStatus: string;
    progressEpisodes?: number;
    progressChapters?: number;
    progressVolumes?: number;
    episodeCount?: number;
    chapterCount?: number;
    volumeCount?: number;
    primaryProgressDimension: string;
    provider: string;
    providerMediaId: string;
    rawListName?: string;
    isConnected: boolean;
    nextReleaseAt?: string;
    nextReleaseLabel?: string;
    lastSyncedAt?: string;
    updatedAt: string;
}

export interface MediaLibraryEntryDetailDto {
    id: string;
    title: MediaLibraryTitleDto;
    provider: string;
    providerMediaId: string;
    providerLibraryEntryId?: string;
    normalizedStatus: string;
    rawStatus?: string;
    rawListName?: string;
    progressEpisodes?: number;
    progressChapters?: number;
    progressVolumes?: number;
    isConnected: boolean;
    nextReleaseAt?: string;
    nextReleaseLabel?: string;
    lastSyncedAt?: string;
    lastRemoteUpdateAt?: string;
    updatedAt: string;
    autoProgressFromObservations: boolean;
    providerLinks: MediaProviderLinkSummaryDto[];
}

export interface MediaLibraryTitleDto {
    id: string;
    canonicalTitle: string;
    originalTitle?: string;
    mediaKind: string;
    synopsis?: string;
    posterUrl?: string;
    startYear?: number;
    episodeCount?: number;
    chapterCount?: number;
    volumeCount?: number;
    primaryProgressDimension: string;
    releaseStatusDimension: string;
}

export interface MediaProviderLinkSummaryDto {
    id: string;
    provider: string;
    externalId: string;
    externalUrl?: string;
    linkSource: string;
    lastVerifiedAt?: string;
}

export interface MediaLinkRequestDto {
    providerId: string;
    providerMediaId: string;
    forceRelink?: boolean;
}

export interface MediaLinkConflictDto {
    error: string;
    conflictingMediaTitleId: string;
    conflictingCanonicalTitle: string;
}

export interface MediaProviderAccountStatusDto {
    providerId: string;
    isConnected: boolean;
    displayName?: string;
    externalAccountId?: string;
    connectedAt?: string;
}

export interface MediaImportDto {
    providerId: string;
    importedCount: number;
    createdTitles: number;
    createdEntries: number;
    updatedEntries: number;
    importedAt: string;
}

export interface MediaProgressUpdateDto {
    progressEpisodes?: number;
    progressChapters?: number;
    progressVolumes?: number;
}

export interface MediaStatusUpdateDto {
    status: string;
}

export interface MediaAutoProgressUpdateDto {
    enabled: boolean;
}

export interface MediaProviderSearchResultDto {
    providerId: string;
    providerMediaId: string;
    title: string;
    nativeTitle?: string;
    mediaKind: string;
    synopsis?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    startYear?: number;
    episodeCount?: number;
    chapterCount?: number;
    volumeCount?: number;
    primaryProgressDimension: string;
    releaseStatusDimension: string;
}

export interface MediaProviderTitleDetailsDto {
    providerId: string;
    providerMediaId: string;
    title: string;
    nativeTitle?: string;
    mediaKind: string;
    synopsis?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    startYear?: number;
    episodeCount?: number;
    chapterCount?: number;
    volumeCount?: number;
    primaryProgressDimension: string;
    releaseStatusDimension: string;
    availabilityLinks: MediaProviderAvailabilityLinkDto[];
}

export interface MediaProviderAvailabilityLinkDto {
    serviceId: string;
    displayName: string;
    url?: string;
    availabilityKind: string;
    notes?: string;
    iconUrl?: string;
}

export interface MediaReleaseMetadataDto {
    providerId: string;
    providerMediaId: string;
    releaseStatusDimension: string;
    releasedCount?: number;
    totalKnownCount?: number;
    nextReleaseAt?: string;
    nextReleaseLabel?: string;
}

export interface MediaLibraryQueryParams {
    status?: string;
    mediaKind?: string;
    provider?: string;
    listName?: string;
    sortBy?: string;
    sortDir?: 'asc' | 'desc';
    page?: number;
    pageSize?: number;
}

export interface MediaProviderSearchParams {
    query: string;
    mediaKinds?: string[];
    limit?: number;
}

export interface MediaApiRuntimeConfig {
    apiBaseUrl?: string;
    accessToken?: string;
    includeCredentials?: boolean;
}