export interface MatchingSummaryResponse {
  awaitingMatching: number;
  needsReview: number;
  matched: number;
  notMusic: number;
}

export interface MatchingQueuePlaylistResponse {
  playlistId: string;
  playlistName: string;
  position: number;
}

export interface MatchingCandidateComparisonResponse {
  label: string;
  observationValue?: string;
  candidateValue?: string;
  score?: number;
  scoreLabel?: string;
  tone: 'match' | 'close' | 'miss' | 'neutral';
}

export interface MatchingQueueCandidateResponse {
  candidateId: string;
  candidateSource: string;
  externalId: string;
  title: string;
  artist?: string;
  mbidRecording?: string;
  isrc?: string;
  durationSeconds?: number;
  score: number;
  explanation?: string;
  isAccepted: boolean;
  versionMarkers: string[];
  playbackModifiers: string[];
  comparisons?: MatchingCandidateComparisonResponse[];
  titleSimilarity?: number;
  artistSimilarity?: number;
  durationScore?: number;
  semanticAdjustment?: number;
  semanticExplanation?: string;
  clusterId?: string;
  clusterSize: number;
  clusterReason?: string;
}

export interface TrackMatchObservationDiagnostics {
  versionMarkers: string[];
  playbackModifiers: string[];
  decisionReason?: string;
  topScore?: number;
  secondDistinctScore?: number;
  distinctClusterCount: number;
}

export interface MatchingQueueItemResponse {
  observationId: string;
  sourceType: string;
  externalId: string;
  title: string;
  artist?: string;
  thumbnailUrl?: string;
  durationSeconds?: number;
  matchStatus: string;
  matchAttemptCount: number;
  lastMatchAttemptedAt?: string;
  lastMatchError?: string;
  resolutionNotes?: string;
  suggestedVersionFlags: number;
  diagnostics: TrackMatchObservationDiagnostics;
  playlists: MatchingQueuePlaylistResponse[];
  candidates: MatchingQueueCandidateResponse[];
}

export interface MatchingQueuePageResponse {
  items: MatchingQueueItemResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface MatchingQueueFilters {
  status?: string;
  sourceType?: string;
  errorsOnly?: boolean;
  query?: string;
}

export interface MatchingBulkRetryResponse {
  queuedCount: number;
}

export interface TrackMatchWorkItemResponse {
  observationId: string;
  sourceType: string;
  externalId: string;
  title: string;
  artist?: string;
  queueStatus: 'ready' | 'processing' | 'scheduled';
  retryCount: number;
  lifetimeAttemptCount: number;
  nextAttemptAt: string;
  leaseExpiresAt?: string;
  lastAttemptedAt?: string;
  lastError?: string;
}

export interface TrackMatchWorkPageResponse {
  items: TrackMatchWorkItemResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  readyCount: number;
  scheduledCount: number;
  processingCount: number;
  providerNotBefore?: string;
  providerCooldownSource?: 'retry-after' | 'fallback';
  asOf: string;
}

export interface SongGroupingTrackResponse {
  trackId: string;
  title?: string;
  artist?: string;
  isrc?: string;
  musicBrainzRecordingId?: string;
  versionFlags: number;
}

export interface SongGroupingSuggestionResponse {
  suggestionId: string;
  confidence: number;
  evidenceJson: string;
  createdAt: string;
  candidate: SongGroupingTrackResponse;
  anchor: SongGroupingTrackResponse;
}

export interface SongGroupingSuggestionPageResponse {
  items: SongGroupingSuggestionResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

class MatchingApiClient {
  private getHeaders(): HeadersInit {
    return {
      'Content-Type': 'application/json',
    };
  }

  private async ensureOk(response: Response, fallbackMessage: string): Promise<void> {
    if (response.ok) {
      return;
    }

    const error = await response.json().catch(() => ({ error: fallbackMessage }));
    throw new Error(error.error || fallbackMessage);
  }

  private async getPage<T>(
    path: string,
    page: number,
    pageSize: number,
    fallbackMessage: string,
  ): Promise<T> {
    const params = new URLSearchParams({
      page: page.toString(),
      pageSize: pageSize.toString(),
    });
    const response = await fetch(`${path}?${params}`, {
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, fallbackMessage);
    return response.json();
  }

  async getSummary(): Promise<MatchingSummaryResponse> {
    const response = await fetch('/api/matching/summary', {
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to load matching summary');
    return response.json();
  }

  async getQueue(page = 1, pageSize = 5, filters: MatchingQueueFilters = {}): Promise<MatchingQueuePageResponse> {
    const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() });
    if (filters.status) params.set('status', filters.status);
    if (filters.sourceType) params.set('sourceType', filters.sourceType);
    if (filters.errorsOnly) params.set('errorsOnly', 'true');
    if (filters.query) params.set('query', filters.query);
    const response = await fetch(`/api/matching/queue?${params}`, {
      credentials: 'include',
      headers: this.getHeaders(),
    });
    await this.ensureOk(response, 'Failed to load matching queue');
    return response.json();
  }

  async getWorkQueue(page = 1, pageSize = 20): Promise<TrackMatchWorkPageResponse> {
    return this.getPage('/api/matching/work-queue', page, pageSize, 'Failed to load matching work queue');
  }

  async retry(observationId: string): Promise<void> {
    const response = await fetch(`/api/matching/queue/${encodeURIComponent(observationId)}/retry`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to retry matching');
  }

  async retryFiltered(filters: MatchingQueueFilters): Promise<MatchingBulkRetryResponse> {
    const response = await fetch('/api/matching/queue/retry-filtered', {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
      body: JSON.stringify(filters),
    });
    await this.ensureOk(response, 'Failed to retry filtered matches');
    return response.json();
  }

  async selectCandidate(observationId: string, candidateId: string): Promise<void> {
    const response = await fetch(`/api/matching/queue/${encodeURIComponent(observationId)}/select-candidate`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
      body: JSON.stringify({ candidateId }),
    });

    await this.ensureOk(response, 'Failed to select matching candidate');
  }

  async selectCandidateAsVersion(
    observationId: string,
    candidateId: string,
    versionFlags: number,
  ): Promise<void> {
    const response = await fetch(
      `/api/matching/queue/${encodeURIComponent(observationId)}/select-candidate-as-version`,
      {
        method: 'POST',
        credentials: 'include',
        headers: this.getHeaders(),
        body: JSON.stringify({ candidateId, versionFlags }),
      },
    );

    await this.ensureOk(response, 'Failed to add matching candidate as a version');
  }

  async markNotMusic(observationId: string): Promise<void> {
    const response = await fetch(`/api/matching/queue/${encodeURIComponent(observationId)}/mark-not-music`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to mark observation as not music');
  }

  async createTrack(observationId: string): Promise<void> {
    const response = await fetch(`/api/matching/queue/${encodeURIComponent(observationId)}/create-track`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to create canonical track');
  }

  async getSongGroupingSuggestions(
    page = 1,
    pageSize = 5,
  ): Promise<SongGroupingSuggestionPageResponse> {
    return this.getPage(
      '/api/matching/song-grouping',
      page,
      pageSize,
      'Failed to load song grouping suggestions',
    );
  }

  async generateSongGroupingSuggestions(): Promise<number> {
    const response = await fetch('/api/matching/song-grouping/generate', {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to generate song grouping suggestions');
    const result = await response.json() as { createdCount: number };
    return result.createdCount;
  }

  async reviewSongGroupingSuggestion(
    suggestionId: string,
    accept: boolean,
  ): Promise<SongGroupingSuggestionResponse> {
    const action = accept ? 'accept' : 'reject';
    const response = await fetch(
      `/api/matching/song-grouping/${encodeURIComponent(suggestionId)}/${action}`,
      {
        method: 'POST',
        credentials: 'include',
        headers: this.getHeaders(),
      },
    );

    await this.ensureOk(response, `Failed to ${action} song grouping suggestion`);
    return response.json();
  }
}

export const matchingApi = new MatchingApiClient();
