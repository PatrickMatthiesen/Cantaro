export type PlaylistSyncService = 'youtube' | 'spotify';
export type PlaylistSyncMode = 'bidirectional' | 'import_only' | 'from_cantaro';
export type PlaylistInitialMode = 'combine' | 'platform' | 'cantaro';

export interface PlaylistSyncLink {
  mappingId: string;
  service: PlaylistSyncService;
  servicePlaylistId: string;
  externalAccountId: string;
  syncMode: PlaylistSyncMode;
  state: string;
  lastSyncStatus: string | null;
  lastSyncedAt: string | null;
  nextAttemptAt: string | null;
  matchingProcessedCount: number;
  matchingTotalCount: number;
  unresolvedCount: number;
  totalCount: number;
  resolvedCount: number;
  lastError: string | null;
  reconnectRequired?: boolean;
  pendingName: string | null;
  canDeleteRemote: boolean;
}

export interface PlaylistSyncDetails {
  playlistId: string;
  name: string;
  syncEnabled: boolean;
  allowDuplicateTracks: boolean;
  nextSyncAt: string | null;
  links: PlaylistSyncLink[];
}

export interface PlaylistUnmatchedEntry {
  entryId: string;
  title: string;
  artist: string;
  durationSeconds: number | null;
  sourceUrl: string | null;
}

export interface PlaylistMatchCandidate {
  candidateToken: string;
  externalId: string;
  title: string;
  artist: string;
  durationSeconds: number | null;
  url: string;
  reason: string | null;
}

export interface PlaylistMatchSearch {
  entry: PlaylistUnmatchedEntry;
  reason: string;
  suggestedQuery: string;
  candidates: PlaylistMatchCandidate[];
}

export interface PlaylistAttachPreview {
  service: PlaylistSyncService;
  servicePlaylistId: string;
  remoteName: string;
  remoteTrackCount: number;
  unavailableItemCount?: number;
  cantaroTrackCount: number;
  additions: number;
  removals: number;
  previewToken: string;
}

export interface PlaylistCreateCandidate {
  servicePlaylistId: string;
  name: string;
  remoteTrackCount: number;
  sharedTrackCount: number | null;
  linkedPlaylistId: string | null;
  comparisonError: string | null;
}

export interface PlaylistCreatePreview {
  candidates: PlaylistCreateCandidate[];
  previewToken: string;
}

export interface PlaylistRenamePreview {
  currentName: string;
  proposedName: string;
  links: Array<{ mappingId: string; service: PlaylistSyncService; currentName: string; willRename: boolean }>;
  previewToken: string;
}

export interface PlaylistNameProposalPreview {
  currentName: string;
  pendingName: string;
  providerName: string;
  previewToken: string;
}

export interface PlatformDisconnectPreview {
  platformId: PlaylistSyncService;
  externalAccountId: string;
  links: Array<{
    mappingId: string;
    playlistId: string;
    playlistName: string;
    servicePlaylistId: string;
    canDeleteRemote: boolean;
    canDeleteCanonicalIfLast: boolean;
  }>;
}

function requestOptions(method: string, body: unknown): RequestInit {
  return {
    method,
    credentials: 'include',
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  };
}

async function responseError(response: Response): Promise<Error> {
  const result = await response.json().catch(() => null);
  const message = result && typeof result === 'object' && 'error' in result && typeof result.error === 'string'
    ? result.error
    : `Playlist sync request failed (${response.status})`;
  return new Error(message);
}

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await fetch(path, requestOptions(method, body));
  if (!response.ok) {
    throw await responseError(response);
  }
  return response.status === 204 ? undefined as T : response.json();
}

function base(playlistId: string) {
  return `/api/music/playlists/${encodeURIComponent(playlistId)}/sync`;
}

export const playlistSyncApi = {
  unmatched: (playlistId: string, mappingId: string) =>
    request<PlaylistUnmatchedEntry[]>(`${base(playlistId)}/links/${encodeURIComponent(mappingId)}/unmatched`),
  searchMatch: (playlistId: string, mappingId: string, entryId: string, query?: string) =>
    request<PlaylistMatchSearch>(`${base(playlistId)}/links/${encodeURIComponent(mappingId)}/unmatched/${encodeURIComponent(entryId)}/search`, 'POST', { query }),
  confirmMatch: (playlistId: string, mappingId: string, entryId: string, candidateToken: string) =>
    request<void>(`${base(playlistId)}/links/${encodeURIComponent(mappingId)}/unmatched/${encodeURIComponent(entryId)}/confirm`, 'POST', { candidateToken }),
  get: (playlistId: string) => request<PlaylistSyncDetails>(base(playlistId)),
  previewAttach: (playlistId: string, service: PlaylistSyncService, servicePlaylistId: string) =>
    request<PlaylistAttachPreview>(`${base(playlistId)}/attach/preview`, 'POST', { service, servicePlaylistId }),
  attach: (playlistId: string, requestBody: {
    service: PlaylistSyncService;
    servicePlaylistId: string;
    syncMode: PlaylistSyncMode;
    initialMode: PlaylistInitialMode;
    previewToken: string;
  }) => request<PlaylistSyncDetails>(`${base(playlistId)}/attach`, 'POST', requestBody),
  previewCreateLink: (playlistId: string, service: PlaylistSyncService) =>
    request<PlaylistCreatePreview>(`${base(playlistId)}/links/create/preview`, 'POST', { service }),
  createLink: (playlistId: string, service: PlaylistSyncService, previewToken?: string) =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/links/create`, 'POST', { service, previewToken }),
  previewInitialization: (playlistId: string, mappingId: string) =>
    request<PlaylistAttachPreview>(`${base(playlistId)}/links/${encodeURIComponent(mappingId)}/initialization/preview`, 'POST'),
  confirmInitialization: (playlistId: string, mappingId: string, previewToken: string) =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/links/${encodeURIComponent(mappingId)}/initialization/confirm`, 'POST', { previewToken }),
  run: (playlistId: string, service: PlaylistSyncService) =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/run`, 'POST', { service }),
  setEnabled: (playlistId: string, syncEnabled: boolean) =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/enabled`, 'PUT', { syncEnabled }),
  setDuplicates: (playlistId: string, allowDuplicateTracks: boolean) =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/duplicates`, 'PUT', { allowDuplicateTracks }),
  previewRename: (playlistId: string, name: string) =>
    request<PlaylistRenamePreview>(`${base(playlistId)}/rename/preview`, 'POST', { name }),
  confirmRename: (playlistId: string, name: string, previewToken: string) =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/rename/confirm`, 'POST', { name, previewToken }),
  previewNameProposal: (playlistId: string, mappingId: string) =>
    request<PlaylistNameProposalPreview>(`${base(playlistId)}/rename/proposals/${encodeURIComponent(mappingId)}/preview`, 'POST'),
  decideNameProposal: (playlistId: string, mappingId: string, accept: boolean, previewToken: string) =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/rename/proposals/${encodeURIComponent(mappingId)}/decision`, 'POST', { accept, previewToken }),
  resolveOrder: (playlistId: string, mappingId: string, use: 'cantaro' | 'platform') =>
    request<PlaylistSyncDetails>(`${base(playlistId)}/links/${encodeURIComponent(mappingId)}/order`, 'POST', { use }),
  unlink: (playlistId: string, mappingId: string, deleteRemote: boolean, deleteCanonicalIfLast: boolean) =>
    request<void>(`${base(playlistId)}/links/${encodeURIComponent(mappingId)}`, 'DELETE', { deleteRemote, deleteCanonicalIfLast }),
  previewDisconnect: (service: PlaylistSyncService) =>
    request<PlatformDisconnectPreview>(`/api/platforms/${service}/disconnect/preview`),
  disconnect: (service: PlaylistSyncService, links: Array<{ mappingId: string; deleteRemote: boolean; deleteCanonicalIfLast: boolean }>) =>
    request<{ disconnected: boolean }>(`/api/platforms/${service}/disconnect`, 'POST', { links }),
};
