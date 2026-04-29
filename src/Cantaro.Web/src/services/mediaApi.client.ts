import {
    normalizeMediaApiBaseUrl,
    resolveMediaApiRuntimeConfig,
} from './mediaApi.runtime';
import type {
    MediaApiRuntimeConfig,
    MediaAutoProgressUpdateDto,
    MediaImportDto,
    MediaLibraryEntryDetailDto,
    MediaLibraryPageDto,
    MediaLibraryQueryParams,
    MediaLinkRequestDto,
    MediaProgressUpdateDto,
    MediaProviderAccountStatusDto,
    MediaProviderSearchParams,
    MediaProviderSearchResultDto,
    MediaProviderTitleDetailsDto,
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
        const { apiBaseUrl } = await this.getRuntimeConfig();
        const baseUrl = normalizeMediaApiBaseUrl(apiBaseUrl);
        return baseUrl ? `${baseUrl}${path}` : path;
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
        throw new Error(body.error || fallbackMessage);
    }

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

        const response = await this.request(`/api/media/library?${query.toString()}`);
        await this.ensureOk(response, 'Failed to load media library');
        return response.json() as Promise<MediaLibraryPageDto>;
    }

    async getLibraryEntry(libraryEntryId: string): Promise<MediaLibraryEntryDetailDto> {
        const response = await this.request(`/api/media/library/${encodeURIComponent(libraryEntryId)}`);
        await this.ensureOk(response, 'Failed to load media library entry');
        return response.json() as Promise<MediaLibraryEntryDetailDto>;
    }

    async linkProvider(libraryEntryId: string, request: MediaLinkRequestDto): Promise<void> {
        const response = await this.request(`/api/media/library/${encodeURIComponent(libraryEntryId)}/link`, {
            method: 'POST',
            body: JSON.stringify(request),
        });
        await this.ensureOk(response, 'Failed to link provider');
    }

    async unlinkProvider(libraryEntryId: string, providerId: string): Promise<void> {
        const response = await this.request(
            `/api/media/library/${encodeURIComponent(libraryEntryId)}/link/${encodeURIComponent(providerId)}`,
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

    async importLibrary(providerId: string): Promise<MediaImportDto> {
        const response = await this.request(`/api/media/providers/${encodeURIComponent(providerId)}/import`, {
            method: 'POST',
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

    async updateProgress(libraryEntryId: string, request: MediaProgressUpdateDto): Promise<void> {
        const response = await this.request(`/api/media/library/${encodeURIComponent(libraryEntryId)}/progress`, {
            method: 'POST',
            body: JSON.stringify(request),
        });
        await this.ensureOk(response, 'Failed to update media progress');
    }

    async updateStatus(libraryEntryId: string, request: MediaStatusUpdateDto): Promise<void> {
        const response = await this.request(`/api/media/library/${encodeURIComponent(libraryEntryId)}/status`, {
            method: 'POST',
            body: JSON.stringify(request),
        });
        await this.ensureOk(response, 'Failed to update media status');
    }

    async updateAutoProgress(
        libraryEntryId: string,
        request: MediaAutoProgressUpdateDto,
    ): Promise<void> {
        const response = await this.request(
            `/api/media/library/${encodeURIComponent(libraryEntryId)}/auto-progress`,
            {
                method: 'PATCH',
                body: JSON.stringify(request),
            },
        );
        await this.ensureOk(response, 'Failed to update auto-progress setting');
    }
}