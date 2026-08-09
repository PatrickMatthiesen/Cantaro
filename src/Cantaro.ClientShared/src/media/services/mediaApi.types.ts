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
    releasedCount?: number;
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
    confirmReplacement?: boolean;
}

export interface MediaLinkConflictDto {
    error: string;
    code: string;
    conflictingMediaTitleId?: string;
    conflictingCanonicalTitle?: string;
    currentProviderMediaId?: string;
}

export interface MediaContinueWatchingDto {
    outcome: 'direct' | 'series_fallback' | 'completed' | 'unavailable' | 'conflict';
    episodeNumber?: number;
    provider?: string;
    url?: string;
}

export interface MediaEpisodeCatalogDto {
    seriesDestinations: MediaStreamingDestinationDto[];
    episodes: MediaEpisodeDestinationDto[];
}

export interface MediaEpisodeDestinationDto {
    episodeNumber: number;
    title?: string;
    destinations: MediaStreamingDestinationDto[];
    seenCount: number;
    hasConflict: boolean;
}

export interface MediaStreamingDestinationDto {
    serviceId: string;
    url: string;
    seenCount: number;
    firstSeenAt: string;
    lastSeenAt: string;
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

export interface MediaImportRequestDto {
    providerId: string;
    importId: string;
    status: string;
}

export interface MediaLibraryImportEventDto {
    providerId: string;
    importId: string;
    status: string;
    importedCount: number;
    createdTitles: number;
    createdEntries: number;
    updatedEntries: number;
    libraryChanged: boolean;
    errorMessage?: string;
    occurredAt: string;
}

export interface MediaProgressUpdateDto {
    progressEpisodes?: number;
    progressChapters?: number;
    progressVolumes?: number;
}

export interface MediaStatusUpdateDto {
    status: string;
}

export interface MediaCatalogAddRequestDto {
    status: string;
}

export interface MediaCatalogAddResultDto {
    libraryEntryId: string;
    mediaTitleId: string;
    status: string;
}

export interface MediaObservationSummaryDto {
    totalUnresolved: number;
    pending: number;
    ambiguous: number;
    noMatch: number;
}

export interface MediaObservationCandidateDto {
    candidateId: string;
    candidateSource: string;
    mediaTitleId: string;
    provider?: string;
    providerMediaId?: string;
    title: string;
    mediaKind: string;
    score: number;
    explanation?: string;
    isAccepted: boolean;
}

export interface MediaObservationDto {
    observationId: string;
    siteIdentifier: string;
    observedUrl: string;
    siteMediaId?: string;
    observedTitle: string;
    progressHint?: string;
    observedAt: string;
    extensionVersion?: string;
    matchStatus: string;
    mediaTitleId?: string;
    resolutionNotes?: string;
    matchAttemptCount: number;
    lastMatchAttemptedAt?: string;
    lastMatchError?: string;
    createdAt: string;
    candidates: MediaObservationCandidateDto[];
}

export interface ResolveMediaObservationDto {
    candidateId: string;
}

export interface MediaCatalogLibraryStateDto {
    isInLibrary: boolean;
    libraryEntryId?: string;
    mediaTitleId?: string;
    normalizedStatus?: string;
    progressEpisodes?: number;
    progressChapters?: number;
    progressVolumes?: number;
}

interface MediaProviderMetadataDto {
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

export interface MediaProviderSearchResultDto extends MediaProviderMetadataDto {
    providerId: string;
    providerMediaId: string;
    libraryState?: MediaCatalogLibraryStateDto;
}

export interface MediaProviderTitleDetailsDto extends MediaProviderMetadataDto {
    mediaTitleId: string;
    providerId: string;
    providerMediaId: string;
    availabilityLinks: MediaProviderAvailabilityLinkDto[];
    characters: MediaProviderCharacterCreditDto[];
    libraryState?: MediaCatalogLibraryStateDto;
}

export interface MediaProviderCharacterCreditDto {
    characterId: string;
    name: string;
    imageUrl?: string;
    role: 'main' | 'supporting' | string;
    providerUrl?: string;
    order: number;
}

export interface MediaProviderAvailabilityLinkDto {
    serviceId: string;
    displayName: string;
    url?: string;
    availabilityKind: string;
    notes?: string;
    iconUrl?: string;
}

export interface MediaLibraryQueryParams {
    query?: string;
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
