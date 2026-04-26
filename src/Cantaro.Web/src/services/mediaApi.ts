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

class MediaApiClient {
  private getHeaders(): HeadersInit {
    return {
      'Content-Type': 'application/json',
    };
  }

  private async ensureOk(response: Response, fallbackMessage: string): Promise<void> {
    if (response.ok) return;
    const body = await response.json().catch(() => ({ error: fallbackMessage }));
    throw new Error(body.error || fallbackMessage);
  }

  // ── Library ──────────────────────────────────────────────────────────────

  async getLibrary(params: MediaLibraryQueryParams = {}): Promise<MediaLibraryPageDto> {
    const query = new URLSearchParams();
    if (params.status) query.set('status', params.status);
    if (params.mediaKind) query.set('mediaKind', params.mediaKind);
    if (params.provider) query.set('provider', params.provider);
    if (params.listName) query.set('listName', params.listName);
    if (params.sortBy) query.set('sortBy', params.sortBy);
    if (params.sortDir) query.set('sortDir', params.sortDir);
    if (params.page !== undefined) query.set('page', String(params.page));
    if (params.pageSize !== undefined) query.set('pageSize', String(params.pageSize));

    const response = await fetch(`/api/media/library?${query.toString()}`, {
      credentials: 'include',
      headers: this.getHeaders(),
    });
    await this.ensureOk(response, 'Failed to load media library');
    return response.json() as Promise<MediaLibraryPageDto>;
  }

  async getLibraryEntry(libraryEntryId: string): Promise<MediaLibraryEntryDetailDto> {
    const response = await fetch(`/api/media/library/${encodeURIComponent(libraryEntryId)}`, {
      credentials: 'include',
      headers: this.getHeaders(),
    });
    await this.ensureOk(response, 'Failed to load media library entry');
    return response.json() as Promise<MediaLibraryEntryDetailDto>;
  }

  async linkProvider(libraryEntryId: string, request: MediaLinkRequestDto): Promise<void> {
    const response = await fetch(`/api/media/library/${encodeURIComponent(libraryEntryId)}/link`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
      body: JSON.stringify(request),
    });
    await this.ensureOk(response, 'Failed to link provider');
  }

  async unlinkProvider(libraryEntryId: string, providerId: string): Promise<void> {
    const response = await fetch(
      `/api/media/library/${encodeURIComponent(libraryEntryId)}/link/${encodeURIComponent(providerId)}`,
      {
        method: 'DELETE',
        credentials: 'include',
        headers: this.getHeaders(),
      },
    );
    await this.ensureOk(response, 'Failed to unlink provider');
  }

  // ── Providers ─────────────────────────────────────────────────────────────

  async getProviderStatus(providerId: string): Promise<MediaProviderAccountStatusDto> {
    const response = await fetch(`/api/media/providers/${encodeURIComponent(providerId)}/status`, {
      credentials: 'include',
      headers: this.getHeaders(),
    });
    await this.ensureOk(response, 'Failed to get media provider status');
    return response.json() as Promise<MediaProviderAccountStatusDto>;
  }

  /** Navigates the browser to the provider OAuth connect flow. */
  connectProvider(providerId: string, options: { route?: string; trigger?: string } = {}): void {
    const query = new URLSearchParams();
    if (options.route) query.set('route', options.route);
    if (options.trigger) query.set('trigger', options.trigger);
    window.location.assign(`/api/media/providers/${encodeURIComponent(providerId)}/connect?${query.toString()}`);
  }

  async disconnectProvider(providerId: string): Promise<void> {
    const response = await fetch(`/api/media/providers/${encodeURIComponent(providerId)}/disconnect`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });
    await this.ensureOk(response, 'Failed to disconnect media provider');
  }

  async importLibrary(providerId: string): Promise<MediaImportDto> {
    const response = await fetch(`/api/media/providers/${encodeURIComponent(providerId)}/import`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });
    await this.ensureOk(response, 'Failed to import media library');
    return response.json() as Promise<MediaImportDto>;
  }

  async searchProvider(
    providerId: string,
    params: MediaProviderSearchParams,
  ): Promise<MediaProviderSearchResultDto[]> {
    const query = new URLSearchParams({ query: params.query });
    if (params.limit !== undefined) query.set('limit', String(params.limit));
    for (const kind of params.mediaKinds ?? []) query.append('mediaKinds', kind);

    const response = await fetch(
      `/api/media/providers/${encodeURIComponent(providerId)}/search?${query.toString()}`,
      {
        credentials: 'include',
        headers: this.getHeaders(),
      },
    );
    await this.ensureOk(response, 'Failed to search media provider');
    return response.json() as Promise<MediaProviderSearchResultDto[]>;
  }

  async getTitleDetails(
    providerId: string,
    providerMediaId: string,
  ): Promise<MediaProviderTitleDetailsDto> {
    const response = await fetch(
      `/api/media/providers/${encodeURIComponent(providerId)}/titles/${encodeURIComponent(providerMediaId)}`,
      {
        credentials: 'include',
        headers: this.getHeaders(),
      },
    );
    await this.ensureOk(response, 'Failed to get title details');
    return response.json() as Promise<MediaProviderTitleDetailsDto>;
  }

  async getReleaseMetadata(
    providerId: string,
    providerMediaId: string,
  ): Promise<MediaReleaseMetadataDto> {
    const response = await fetch(
      `/api/media/providers/${encodeURIComponent(providerId)}/titles/${encodeURIComponent(providerMediaId)}/release`,
      {
        credentials: 'include',
        headers: this.getHeaders(),
      },
    );
    await this.ensureOk(response, 'Failed to get release metadata');
    return response.json() as Promise<MediaReleaseMetadataDto>;
  }

  // ── Progress & status updates ──────────────────────────────────────────────

  async updateProgress(libraryEntryId: string, request: MediaProgressUpdateDto): Promise<void> {
    const response = await fetch(`/api/media/library/${encodeURIComponent(libraryEntryId)}/progress`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
      body: JSON.stringify(request),
    });
    await this.ensureOk(response, 'Failed to update media progress');
  }

  async updateStatus(libraryEntryId: string, request: MediaStatusUpdateDto): Promise<void> {
    const response = await fetch(`/api/media/library/${encodeURIComponent(libraryEntryId)}/status`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
      body: JSON.stringify(request),
    });
    await this.ensureOk(response, 'Failed to update media status');
  }

  async updateAutoProgress(
    libraryEntryId: string,
    request: MediaAutoProgressUpdateDto,
  ): Promise<void> {
    const response = await fetch(
      `/api/media/library/${encodeURIComponent(libraryEntryId)}/auto-progress`,
      {
        method: 'PATCH',
        credentials: 'include',
        headers: this.getHeaders(),
        body: JSON.stringify(request),
      },
    );
    await this.ensureOk(response, 'Failed to update auto-progress setting');
  }
}

export const mediaApi = new MediaApiClient();
