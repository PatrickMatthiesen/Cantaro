export interface MatchingSummaryResponse {
  totalUnresolved: number;
  pending: number;
  ambiguous: number;
  noMatch: number;
}

export interface MatchingQueuePlaylistResponse {
  playlistId: string;
  playlistName: string;
  position: number;
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
  playlists: MatchingQueuePlaylistResponse[];
  candidates: MatchingQueueCandidateResponse[];
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

  async getSummary(): Promise<MatchingSummaryResponse> {
    const response = await fetch('/api/matching/summary', {
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to load matching summary');
    return response.json();
  }

  async getQueue(): Promise<MatchingQueueItemResponse[]> {
    const response = await fetch('/api/matching/queue', {
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to load matching queue');
    return response.json();
  }

  async retry(observationId: string): Promise<void> {
    const response = await fetch(`/api/matching/queue/${encodeURIComponent(observationId)}/retry`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to retry matching');
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

  async markNoMatch(observationId: string): Promise<void> {
    const response = await fetch(`/api/matching/queue/${encodeURIComponent(observationId)}/mark-no-match`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to mark observation as no match');
  }

  async createTrack(observationId: string): Promise<void> {
    const response = await fetch(`/api/matching/queue/${encodeURIComponent(observationId)}/create-track`, {
      method: 'POST',
      credentials: 'include',
      headers: this.getHeaders(),
    });

    await this.ensureOk(response, 'Failed to create canonical track');
  }
}

export const matchingApi = new MatchingApiClient();
