import {
    normalizeMediaApiBaseUrl,
    resolveMediaApiRuntimeConfig,
} from './mediaApi.runtime';
import type {
    MediaApiRuntimeConfig,
    MediaViewerStateCreateDto,
    MediaImportRequestDto,
    MediaFranchiseGraphDto,
    MediaTitleDetailDto,
    MediaViewerStateDto,
    MediaLibraryImportEventDto,
    MediaInitialSyncApplyRequestDto,
    MediaInitialSyncApplyResultDto,
    MediaInitialSyncPreviewDto,
    MediaInitialSyncProgressDto,
    MediaLibraryPageDto,
    MediaLibraryQueryParams,
    MediaLinkRequestDto,
    MediaContinueWatchingDto,
    MediaEpisodeCatalogDto,
    MediaObservationDto,
    MediaObservationSummaryDto,
    MediaProgressUpdateDto,
    MediaScoreUpdateDto,
    MediaProviderAccountStatusDto,
    MediaProviderSearchParams,
    MediaProviderSearchResultDto,
    MediaProviderTitleDetailsDto,
    ResolveMediaObservationDto,
    MediaStatusUpdateDto,
} from './mediaApi.types';

export class MediaApiClient {
    private async getRuntimeConfig(): Promise<MediaApiRuntimeConfig> {
        return resolveMediaApiRuntimeConfig();
    }

    private async getHeaders(): Promise<HeadersInit> {
        const { accessToken } = await this.getRuntimeConfig();
        return {
            'Content-Type': 'application/json',
            ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        };
    }

    private async buildUrl(path: string): Promise<string> {
        const { baseUrl } = await this.getRuntimeConfig();
        const normalizedBaseUrl = normalizeMediaApiBaseUrl(baseUrl);
        return baseUrl ? `${normalizedBaseUrl}${path}` : path;
    }

    private async getCredentialsMode(): Promise<RequestCredentials> {
        const { accessToken, includeCredentials = true } = await this.getRuntimeConfig();
        if (accessToken) {
            return 'omit';
        }

        return includeCredentials ? 'include' : 'omit';
    }

    private async request(path: string, init: RequestInit = {}): Promise<Response> {
        return fetch(await this.buildUrl(path), {
            ...init,
            credentials: await this.getCredentialsMode(),
            headers: {
                ...(await this.getHeaders()),
                ...(init.headers ?? {}),
            },
        });
    }

    private async ensureOk(response: Response, fallbackMessage: string): Promise<void> {
        if (response.ok) return;
        const body = await response.json().catch(() => ({ error: fallbackMessage }));
        throw Object.assign(new Error(body.error || fallbackMessage), {
            status: response.status,
            responseBody: body,
        });
    }

    async openLibraryEvents(signal: AbortSignal): Promise<Response> {
        return this.request('/api/media/library/events', {
            signal,
            headers: { Accept: 'text/event-stream' },
            cache: 'no-store',
        });
    }

    async getLibrary(params: MediaLibraryQueryParams = {}): Promise<MediaLibraryPageDto> {
        const query = new URLSearchParams();
        for (const [key, value] of Object.entries(params)) {
            if (value !== undefined) {
                query.set(key, String(value));
            }
        }

        const response = await this.request(`/api/media/library?${query.toString()}`);
        await this.ensureOk(response, 'Failed to load media library');
        return response.json() as Promise<MediaLibraryPageDto>;
    }

    async getMediaTitle(mediaTitleId: string): Promise<MediaTitleDetailDto> {
        const response = await this.request(`/api/media/titles/${encodeURIComponent(mediaTitleId)}`);
        await this.ensureOk(response, 'Failed to load media title');
        return response.json() as Promise<MediaTitleDetailDto>;
    }

    async getViewerState(mediaTitleId: string): Promise<MediaViewerStateDto | null> {
        const response = await this.request(`/api/media/titles/${encodeURIComponent(mediaTitleId)}/viewer`);
        if (response.status === 401) return null;
        await this.ensureOk(response, 'Failed to load your media state');
        return response.json() as Promise<MediaViewerStateDto | null>;
    }

    async getFranchiseGraph(mediaTitleId: string): Promise<MediaFranchiseGraphDto> {
        const response = await this.request(
            `/api/media/titles/${encodeURIComponent(mediaTitleId)}/franchise`,
        );
        await this.ensureOk(response, 'Failed to load franchise connections');
        return response.json() as Promise<MediaFranchiseGraphDto>;
    }

    async getContinueWatching(mediaTitleId: string): Promise<MediaContinueWatchingDto> {
        const response = await this.request(
            `/api/media/titles/${encodeURIComponent(mediaTitleId)}/viewer/continue-watching`,
        );
        await this.ensureOk(response, 'Failed to resolve the next episode');
        return response.json() as Promise<MediaContinueWatchingDto>;
    }

    async getEpisodes(mediaTitleId: string): Promise<MediaEpisodeCatalogDto> {
        const response = await this.request(
            `/api/media/titles/${encodeURIComponent(mediaTitleId)}/episodes`,
        );
        await this.ensureOk(response, 'Failed to load episode links');
        return response.json() as Promise<MediaEpisodeCatalogDto>;
    }

    async linkProvider(mediaTitleId: string, request: MediaLinkRequestDto): Promise<void> {
        const response = await this.request(`/api/media/titles/${encodeURIComponent(mediaTitleId)}/links`, {
            method: 'POST',
            body: JSON.stringify(request),
        });
        await this.ensureOk(response, 'Failed to link provider');
    }

    async unlinkProvider(mediaTitleId: string, providerId: string): Promise<void> {
        const response = await this.request(
            `/api/media/titles/${encodeURIComponent(mediaTitleId)}/links/${encodeURIComponent(providerId)}`,
            {
                method: 'DELETE',
            },
        );
        await this.ensureOk(response, 'Failed to unlink provider');
    }

    async getProviderStatus(providerId: string): Promise<MediaProviderAccountStatusDto> {
        const response = await this.request(`/api/media/providers/${encodeURIComponent(providerId)}/status`);
        await this.ensureOk(response, 'Failed to get media provider status');
        return response.json() as Promise<MediaProviderAccountStatusDto>;
    }

    async connectProvider(providerId: string, options: { route?: string; trigger?: string } = {}): Promise<void> {
        const query = new URLSearchParams();
        if (options.route) query.set('route', options.route);
        if (options.trigger) query.set('trigger', options.trigger);
        window.location.assign(await this.buildUrl(`/api/media/providers/${encodeURIComponent(providerId)}/connect?${query.toString()}`));
    }

    async disconnectProvider(providerId: string): Promise<void> {
        const response = await this.request(`/api/media/providers/${encodeURIComponent(providerId)}/disconnect`, {
            method: 'POST',
        });
        await this.ensureOk(response, 'Failed to disconnect media provider');
    }

    async previewInitialSync(providerId: string, signal?: AbortSignal): Promise<MediaInitialSyncPreviewDto> {
        const response = await this.request(
            `/api/media/providers/${encodeURIComponent(providerId)}/initial-sync/preview`,
            { method: 'POST', signal },
        );
        await this.ensureOk(response, 'Failed to prepare provider sync');
        return response.json() as Promise<MediaInitialSyncPreviewDto>;
    }

    async getInitialSyncProgress(providerId: string, batchId: string): Promise<MediaInitialSyncProgressDto> {
        const response = await this.request(
            `/api/media/providers/${encodeURIComponent(providerId)}/initial-sync/batches/${encodeURIComponent(batchId)}`,
        );
        await this.ensureOk(response, 'Failed to check provider sync progress');
        return response.json() as Promise<MediaInitialSyncProgressDto>;
    }

    async applyInitialSync(
        providerId: string,
        request: MediaInitialSyncApplyRequestDto,
    ): Promise<MediaInitialSyncApplyResultDto> {
        const response = await this.request(
            `/api/media/providers/${encodeURIComponent(providerId)}/initial-sync/apply`,
            {
                method: 'POST',
                body: JSON.stringify(request),
            },
        );
        await this.ensureOk(response, 'Failed to sync Cantaro to the provider');
        return response.json() as Promise<MediaInitialSyncApplyResultDto>;
    }

    async importLibrary(providerId: string): Promise<MediaImportRequestDto> {
        const response = await this.request(`/api/media/providers/${encodeURIComponent(providerId)}/import`, {
            method: 'POST',
        });
        await this.ensureOk(response, 'Failed to import media library');
        return response.json() as Promise<MediaImportRequestDto>;
    }

    async subscribeToImportEvents(
        providerId: string,
        importId: string | undefined,
        onEvent: (event: MediaLibraryImportEventDto) => void,
        onError?: () => void,
    ): Promise<EventSource> {
        const { accessToken } = await this.getRuntimeConfig();
        const query = new URLSearchParams();
        if (importId) query.set('importId', importId);
        if (accessToken) query.set('access_token', accessToken);
        const suffix = query.size > 0 ? `?${query.toString()}` : '';

        const eventSource = new EventSource(
            await this.buildUrl(`/api/media/providers/${encodeURIComponent(providerId)}/import/events${suffix}`),
            { withCredentials: await this.getCredentialsMode() === 'include' },
        );

        eventSource.onmessage = (message) => {
            onEvent(JSON.parse(message.data) as MediaLibraryImportEventDto);
        };
        eventSource.onerror = () => {
            onError?.();
        };

        return eventSource;
    }

    async searchProvider(
        providerId: string,
        params: MediaProviderSearchParams,
    ): Promise<MediaProviderSearchResultDto[]> {
        const query = new URLSearchParams({ query: params.query });
        if (params.limit !== undefined) query.set('limit', String(params.limit));
        for (const kind of params.mediaKinds ?? []) query.append('mediaKinds', kind);

        const response = await this.request(
            `/api/media/providers/${encodeURIComponent(providerId)}/search?${query.toString()}`,
        );
        await this.ensureOk(response, 'Failed to search media provider');
        return response.json() as Promise<MediaProviderSearchResultDto[]>;
    }

    async getTitleDetails(
        providerId: string,
        providerMediaId: string,
    ): Promise<MediaProviderTitleDetailsDto> {
        const response = await this.request(
            `/api/media/providers/${encodeURIComponent(providerId)}/titles/${encodeURIComponent(providerMediaId)}`,
        );
        await this.ensureOk(response, 'Failed to get title details');
        return response.json() as Promise<MediaProviderTitleDetailsDto>;
    }

    async addToLibrary(
        mediaTitleId: string,
        request: MediaViewerStateCreateDto,
    ): Promise<MediaViewerStateDto> {
        const response = await this.request(
            `/api/media/titles/${encodeURIComponent(mediaTitleId)}/viewer`,
            {
                method: 'POST',
                body: JSON.stringify(request),
            },
        );
        await this.ensureOk(response, 'Failed to add media to library');
        return response.json() as Promise<MediaViewerStateDto>;
    }

    async updateProgress(mediaTitleId: string, request: MediaProgressUpdateDto): Promise<void> {
        const response = await this.request(`/api/media/titles/${encodeURIComponent(mediaTitleId)}/viewer/progress`, {
            method: 'POST',
            body: JSON.stringify(request),
        });
        await this.ensureOk(response, 'Failed to update media progress');
    }

    async updateStatus(mediaTitleId: string, request: MediaStatusUpdateDto): Promise<void> {
        const response = await this.request(`/api/media/titles/${encodeURIComponent(mediaTitleId)}/viewer/status`, {
            method: 'POST',
            body: JSON.stringify(request),
        });
        await this.ensureOk(response, 'Failed to update media status');
    }

    async updateScore(mediaTitleId: string, request: MediaScoreUpdateDto): Promise<void> {
        const response = await this.request(`/api/media/titles/${encodeURIComponent(mediaTitleId)}/viewer/score`, {
            method: 'POST',
            body: JSON.stringify(request),
        });
        await this.ensureOk(response, 'Failed to update media score');
    }

    async getObservationSummary(): Promise<MediaObservationSummaryDto> {
        const response = await this.request('/api/media/observations/summary');
        await this.ensureOk(response, 'Failed to load media observation summary');
        return response.json() as Promise<MediaObservationSummaryDto>;
    }

    async getObservations(options: { includeResolved?: boolean; limit?: number } = {}): Promise<MediaObservationDto[]> {
        const query = new URLSearchParams();
        if (options.includeResolved !== undefined) query.set('includeResolved', String(options.includeResolved));
        if (options.limit !== undefined) query.set('limit', String(options.limit));

        const suffix = query.size > 0 ? `?${query.toString()}` : '';
        const response = await this.request(`/api/media/observations${suffix}`);
        await this.ensureOk(response, 'Failed to load media observations');
        return response.json() as Promise<MediaObservationDto[]>;
    }

    async resolveObservation(
        observationId: string,
        request: ResolveMediaObservationDto,
    ): Promise<MediaObservationDto> {
        const response = await this.request(
            `/api/media/observations/${encodeURIComponent(observationId)}/resolve`,
            {
                method: 'POST',
                body: JSON.stringify(request),
            },
        );
        await this.ensureOk(response, 'Failed to resolve media observation');
        return response.json() as Promise<MediaObservationDto>;
    }

    async rejectObservation(observationId: string): Promise<MediaObservationDto> {
        const response = await this.request(
            `/api/media/observations/${encodeURIComponent(observationId)}/reject`,
            { method: 'POST' },
        );
        await this.ensureOk(response, 'Failed to reject media observation');
        return response.json() as Promise<MediaObservationDto>;
    }

    async retryObservation(observationId: string): Promise<MediaObservationDto> {
        const response = await this.request(
            `/api/media/observations/${encodeURIComponent(observationId)}/retry`,
            { method: 'POST' },
        );
        await this.ensureOk(response, 'Failed to retry media observation matching');
        return response.json() as Promise<MediaObservationDto>;
    }
}
