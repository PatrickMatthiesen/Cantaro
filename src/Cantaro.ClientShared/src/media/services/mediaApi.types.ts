// DTOs mirroring the backend contracts in Cantaro.Api.Models

export interface MediaLibraryPageDto {
    items: MediaLibraryListItemDto[];
    availableProviderListNames: string[];
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
    backgroundUrl?: string;
    mediaKind: string;
    status: string;
    score: number | null;
    progressEpisodes?: number;
    progressChapters?: number;
    progressVolumes?: number;
    episodeCount?: number;
    chapterCount?: number;
    volumeCount?: number;
    releasedCount?: number;
    totalKnownCount?: number;
    availableReleasedCount?: number;
    primaryProgressDimension: string;
    provider: string;
    providerMediaId: string;
    providerListNames: string[];
    isConnected: boolean;
    nextReleaseAt?: string;
    nextReleaseLabel?: string;
    lastSyncedAt?: string;
    updatedAt: string;
}

export interface MediaTitleDetailDto {
    id: string;
    canonicalTitle: string;
    originalTitle?: string;
    synonyms: string[];
    mediaKind: string;
    format?: string;
    synopsis?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    startYear?: number;
    episodeCount?: number;
    chapterCount?: number;
    volumeCount?: number;
    releasedCount?: number;
    totalKnownCount?: number;
    nextReleaseAt?: string;
    nextReleaseLabel?: string;
    primaryProgressDimension: string;
    releaseStatusDimension: string;
    updatedAt: string;
    providerLinks: MediaProviderLinkSummaryDto[];
}

export interface MediaViewerStateDto {
    id: string;
    mediaTitleId: string;
    status: string;
    score: number | null;
    progressEpisodes?: number;
    progressChapters?: number;
    progressVolumes?: number;
    lastLocalEditAt?: string;
    updatedAt: string;
    providerBindings: MediaViewerProviderBindingDto[];
}

export interface MediaViewerProviderBindingDto {
    id: string;
    provider: string;
    providerMediaId: string;
    providerLibraryEntryId?: string;
    providerListNames: string[];
    isConnected: boolean;
    lastSyncedAt?: string;
    lastRemoteUpdateAt?: string;
}

/**
 * Client-side presentation model composed from independently fetched global
 * title data and nullable viewer state. This is not an API DTO.
 */
export interface MediaEntryDetailModel {
    id: string;
    mediaTitleId: string;
    viewerStateStatus: 'loading' | 'loaded' | 'error';
    isInLibrary: boolean;
    title: MediaTitleDetailDto;
    provider: string;
    providerMediaId: string;
    status: string;
    score: number | null;
    providerListNames: string[];
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

export interface MediaFranchiseGraphDto {
    currentMediaTitleId: string;
    sourceProvider: string;
    refreshedAt?: string;
    nodes: MediaFranchiseNodeDto[];
    relations: MediaFranchiseRelationDto[];
    continuity: MediaFranchiseContinuityDto;
}

export interface MediaFranchiseNodeDto {
    mediaTitleId: string;
    provider: string;
    providerMediaId: string;
    externalUrl?: string;
    canonicalTitle: string;
    originalTitle?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    mediaKind: string;
    mediaFormat?: string;
    startYear?: number;
    episodeCount?: number;
    isInLibrary: boolean;
    viewerStatus?: string;
    progressEpisodes?: number | null;
    isCurrent: boolean;
}

export interface MediaFranchiseRelationDto {
    sourceMediaTitleId: string;
    targetMediaTitleId: string;
    relationType: string;
    isEpisodeContinuity: boolean;
}

export interface MediaFranchiseContinuityDto {
    orderedMediaTitleIds: string[];
    episodeOffsetByMediaTitleId: Record<string, number>;
    isComplete: boolean;
}

export interface MediaProviderLinkSummaryDto {
    id: string;
    provider: string;
    externalId: string;
    externalUrl?: string;
    linkSource: string;
    lastVerifiedAt?: string;
    availabilityLinks?: MediaProviderAvailabilityLinkDto[];
    availabilityLastVerifiedAt?: string;
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
    releaseAvailability?: MediaReleaseAvailabilityDto;
}

export interface MediaReleaseAvailabilityDto {
    maxReleasedEpisodes?: number | null;
    languages: MediaReleaseLanguageAvailabilityDto[];
}

export interface MediaReleaseLanguageAvailabilityDto {
    languageCode: string;
    subReleasedEpisodes?: number | null;
    dubReleasedEpisodes?: number | null;
}

export interface MediaEpisodeDestinationDto {
    episodeNumber: number;
    title?: string;
    destinations: MediaStreamingDestinationDto[];
    availableAudioLanguageCodes?: string[];
    availableSubtitleLanguageCodes?: string[];
    seenCount: number;
    hasConflict: boolean;
}

export interface MediaStreamingDestinationDto {
    serviceId: string;
    url: string;
    audioLocale?: string;
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

export interface MediaInitialSyncUnresolvedTitleDto {
    mediaTitleId: string;
    title: string;
    mediaKind: string;
}

export interface MediaInitialSyncPreviewDto {
    providerId: string;
    status: 'ready' | 'settling' | 'blocked' | 'import-required';
    fingerprint?: string;
    refreshedProviderIds: string[];
    willAdd: number;
    willUpdate: number;
    alreadyAligned: number;
    providerOnly: number;
    needsMatching: number;
    pendingOperations: number;
    message?: string;
    unresolvedTitles: MediaInitialSyncUnresolvedTitleDto[];
    generatedAt: string;
}

export interface MediaInitialSyncApplyRequestDto {
    fingerprint: string;
}

export interface MediaInitialSyncApplyResultDto {
    providerId: string;
    status: 'queued' | 'completed' | 'failed';
    added: number;
    updated: number;
    alreadyAligned: number;
    providerOnly: number;
    needsMatching: number;
    queuedOperations: number;
    batchId?: string;
    failedOperations?: number;
    generatedAt: string;
}

export interface MediaInitialSyncProgressDto {
    providerId: string;
    status: 'running' | 'completed' | 'failed';
    pendingOperations: number;
    failedOperations: number;
    checkedAt: string;
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

export interface MediaScoreUpdateDto {
    score: number | null;
}

export interface MediaViewerStateCreateDto {
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
    viewerStateId?: string;
    mediaTitleId?: string;
    status?: string;
    score?: number | null;
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
    /** `fresh` when the provider was reached; `stale` when a cached snapshot was returned. */
    availabilityStatus?: string;
    availabilityLastVerifiedAt?: string;
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
    providerListName?: string;
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
    baseUrl?: string;
    accessToken?: string;
    includeCredentials?: boolean;
}
