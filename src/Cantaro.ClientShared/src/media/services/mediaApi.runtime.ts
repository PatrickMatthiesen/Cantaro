import type { MediaApiRuntimeConfig } from './mediaApi.types';

/** @internal */
export type MediaApiConfigResolver = () => MediaApiRuntimeConfig | Promise<MediaApiRuntimeConfig>;

const defaultMediaApiConfigResolver: MediaApiConfigResolver = () => ({
    baseUrl: '',
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

export function normalizeMediaApiBaseUrl(baseUrl?: string): string {
    const trimmed = baseUrl?.trim();
    return trimmed ? trimmed.replace(/\/+$/, '') : '';
}