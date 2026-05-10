import type { MediaApiRuntimeConfig } from './mediaApi.types';

/** @internal */
export type MediaApiConfigResolver = () => MediaApiRuntimeConfig | Promise<MediaApiRuntimeConfig>;

const defaultMediaApiConfigResolver: MediaApiConfigResolver = () => ({
    apiBaseUrl: '',
    includeCredentials: true,
});

let mediaApiConfigResolver: MediaApiConfigResolver = defaultMediaApiConfigResolver;

/** @internal */
export function configureMediaApi(resolver: MediaApiConfigResolver): void {
    mediaApiConfigResolver = resolver;
}

export async function resolveMediaApiRuntimeConfig(): Promise<MediaApiRuntimeConfig> {
    return mediaApiConfigResolver();
}

export function normalizeMediaApiBaseUrl(apiBaseUrl?: string): string {
    const trimmed = apiBaseUrl?.trim();
    return trimmed ? trimmed.replace(/\/+$/, '') : '';
}