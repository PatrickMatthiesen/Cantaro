declare const __CANTARO_DEFAULT_API_BASE_URL__: string;

export interface ExtensionConfig {
    apiBaseUrl: string;
    accessToken: string;
    refreshToken: string;
    accessTokenExpiresAt: string;
    sessionEmail: string;
}

export function normalizeApiBaseUrl(value: string | null | undefined): string {
    const trimmed = value?.trim();
    return trimmed ? trimmed.replace(/\/+$/, '') : '';
}

export const DEFAULT_API_BASE_URL = normalizeApiBaseUrl(__CANTARO_DEFAULT_API_BASE_URL__) || 'http://localhost:5000';

export const emptyExtensionConfig: ExtensionConfig = {
    apiBaseUrl: DEFAULT_API_BASE_URL,
    accessToken: '',
    refreshToken: '',
    accessTokenExpiresAt: '',
    sessionEmail: '',
};

export async function readExtensionConfig(): Promise<ExtensionConfig> {
    const stored = await browser.storage.local.get([
        'apiBaseUrl',
        'accessToken',
        'refreshToken',
        'accessTokenExpiresAt',
        'sessionEmail',
    ]);

    return {
        apiBaseUrl: normalizeApiBaseUrl(
            typeof stored.apiBaseUrl === 'string' ? stored.apiBaseUrl : DEFAULT_API_BASE_URL,
        ) || DEFAULT_API_BASE_URL,
        accessToken: typeof stored.accessToken === 'string' ? stored.accessToken : '',
        refreshToken: typeof stored.refreshToken === 'string' ? stored.refreshToken : '',
        accessTokenExpiresAt: typeof stored.accessTokenExpiresAt === 'string' ? stored.accessTokenExpiresAt : '',
        sessionEmail: typeof stored.sessionEmail === 'string' ? stored.sessionEmail : '',
    };
}

export async function saveExtensionConfig(config: ExtensionConfig): Promise<ExtensionConfig> {
    const persistedConfig: ExtensionConfig = {
        apiBaseUrl: normalizeApiBaseUrl(config.apiBaseUrl) || DEFAULT_API_BASE_URL,
        accessToken: config.accessToken.trim(),
        refreshToken: config.refreshToken.trim(),
        accessTokenExpiresAt: config.accessTokenExpiresAt.trim(),
        sessionEmail: config.sessionEmail.trim(),
    };

    await browser.storage.local.set({
        apiBaseUrl: persistedConfig.apiBaseUrl,
        accessToken: persistedConfig.accessToken || null,
        refreshToken: persistedConfig.refreshToken || null,
        accessTokenExpiresAt: persistedConfig.accessTokenExpiresAt || null,
        sessionEmail: persistedConfig.sessionEmail || null,
    });

    return persistedConfig;
}

export async function clearExtensionSession(apiBaseUrl?: string): Promise<ExtensionConfig> {
    return saveExtensionConfig({
        apiBaseUrl: normalizeApiBaseUrl(apiBaseUrl) || DEFAULT_API_BASE_URL,
        accessToken: '',
        refreshToken: '',
        accessTokenExpiresAt: '',
        sessionEmail: '',
    });
}