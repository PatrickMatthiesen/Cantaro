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

export function apiBaseUrlOriginMatchPattern(value: string | null | undefined): string | null {
    const normalized = normalizeApiBaseUrl(value);
    if (!normalized) {
        return null;
    }

    try {
        const url = new URL(normalized);
        if (url.protocol !== 'http:' && url.protocol !== 'https:') {
            return null;
        }

        return `${url.origin}/*`;
    } catch {
        return null;
    }
}

export const DEFAULT_API_BASE_URL = normalizeApiBaseUrl(__CANTARO_DEFAULT_API_BASE_URL__) || 'http://localhost:5000';

export async function ensureApiBaseUrlPermission(apiBaseUrl: string): Promise<void> {
    const originMatchPattern = apiBaseUrlOriginMatchPattern(apiBaseUrl);
    if (!originMatchPattern || !browser.permissions?.contains || !browser.permissions?.request) {
        return;
    }

    const permissionRequest = { origins: [originMatchPattern] };
    const alreadyGranted = await browser.permissions.contains(permissionRequest);
    if (alreadyGranted) {
        return;
    }

    const granted = await browser.permissions.request(permissionRequest);
    if (!granted) {
        throw new Error(`Access to ${new URL(apiBaseUrl).origin} was not granted.`);
    }
}

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