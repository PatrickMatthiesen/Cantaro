import {
    clearExtensionSession,
    DEFAULT_API_BASE_URL,
    normalizeApiBaseUrl,
    readExtensionConfig,
    saveExtensionConfig,
    type ExtensionConfig,
} from './extensionRuntimeConfig';

export interface ExtensionUser {
    email: string;
}

interface TokenExchangeResponse {
    accessToken: string;
    refreshToken: string;
    expiresIn: number;
    userEmail: string;
}

const ACCESS_TOKEN_REFRESH_SKEW_MS = 60_000;

function readErrorMessage(payload: unknown, fallbackMessage: string): string {
    if (!payload || typeof payload !== 'object') {
        return fallbackMessage;
    }

    const record = payload as Record<string, unknown>;
    const candidates = [
        record.error_description,
        record.message,
        record.error,
        record.detail,
        record.title,
    ];
    const firstMessage = candidates.find((value) => typeof value === 'string' && value.trim());
    return typeof firstMessage === 'string' ? firstMessage : fallbackMessage;
}

function readStringValue(record: Record<string, unknown>, keys: string[]): string {
    for (const key of keys) {
        const value = record[key];
        if (typeof value === 'string' && value.trim()) {
            return value.trim();
        }
    }

    return '';
}

function parseTokenExchangeResponse(payload: unknown): TokenExchangeResponse {
    if (!payload || typeof payload !== 'object') {
        throw new Error('Cantaro API did not return an extension token response.');
    }

    const record = payload as Record<string, unknown>;
    const accessToken = readStringValue(record, ['access_token', 'accessToken', 'token']);
    const refreshToken = readStringValue(record, ['refresh_token', 'refreshToken']);
    const expiresIn = typeof record.expires_in === 'number'
        ? record.expires_in
        : typeof record.expiresIn === 'number'
            ? record.expiresIn
            : 0;
    const user = record.user && typeof record.user === 'object'
        ? record.user as Record<string, unknown>
        : null;
    const userEmail = user ? readStringValue(user, ['email']) : '';

    if (!accessToken || !refreshToken || !expiresIn) {
        throw new Error('Cantaro API did not return the expected extension session payload.');
    }

    return {
        accessToken,
        refreshToken,
        expiresIn,
        userEmail,
    };
}

function encodeBase64Url(bytes: Uint8Array): string {
    let binary = '';
    for (const byte of bytes) {
        binary += String.fromCharCode(byte);
    }

    return btoa(binary)
        .replace(/\+/g, '-')
        .replace(/\//g, '_')
        .replace(/=+$/g, '');
}

function createRandomBase64Url(byteLength: number): string {
    const bytes = crypto.getRandomValues(new Uint8Array(byteLength));
    return encodeBase64Url(bytes);
}

async function createPkceChallenge(codeVerifier: string): Promise<string> {
    const verifierBytes = new TextEncoder().encode(codeVerifier);
    const hashBuffer = await crypto.subtle.digest('SHA-256', verifierBytes);
    return encodeBase64Url(new Uint8Array(hashBuffer));
}

function getAccessTokenExpiresAt(expiresInSeconds: number): string {
    return new Date(Date.now() + expiresInSeconds * 1000).toISOString();
}

function isAccessTokenFresh(config: ExtensionConfig): boolean {
    if (!config.accessToken || !config.accessTokenExpiresAt) {
        return false;
    }

    const expiresAt = Date.parse(config.accessTokenExpiresAt);
    if (Number.isNaN(expiresAt)) {
        return false;
    }

    return expiresAt - Date.now() > ACCESS_TOKEN_REFRESH_SKEW_MS;
}

async function postTokenGrant(apiBaseUrl: string, params: URLSearchParams): Promise<TokenExchangeResponse> {
    const response = await fetch(`${normalizeApiBaseUrl(apiBaseUrl)}/api/auth/extension/token`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
        },
        body: params.toString(),
    });

    const payload = await response.json().catch(() => null);
    if (!response.ok) {
        throw new Error(readErrorMessage(payload, 'Extension sign-in failed.'));
    }

    return parseTokenExchangeResponse(payload);
}

async function persistTokenExchange(apiBaseUrl: string, tokenResponse: TokenExchangeResponse): Promise<ExtensionConfig> {
    return saveExtensionConfig({
        apiBaseUrl: normalizeApiBaseUrl(apiBaseUrl) || DEFAULT_API_BASE_URL,
        accessToken: tokenResponse.accessToken,
        refreshToken: tokenResponse.refreshToken,
        accessTokenExpiresAt: getAccessTokenExpiresAt(tokenResponse.expiresIn),
        sessionEmail: tokenResponse.userEmail,
    });
}

async function fetchCurrentUser(apiBaseUrl: string, accessToken: string): Promise<ExtensionUser> {
    const response = await fetch(`${normalizeApiBaseUrl(apiBaseUrl)}/api/auth/me`, {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${accessToken}`,
        },
    });

    const payload = await response.json().catch(() => null);
    if (!response.ok) {
        throw new Error(readErrorMessage(payload, 'Failed to verify signed-in user.'));
    }

    const email = payload && typeof payload === 'object' && typeof (payload as Record<string, unknown>).email === 'string'
        ? (payload as Record<string, string>).email
        : '';

    if (!email) {
        throw new Error('Signed-in user payload did not include an email address.');
    }

    return { email };
}

export async function beginInteractiveSignIn(apiBaseUrl: string): Promise<ExtensionConfig> {
    const normalizedApiBaseUrl = normalizeApiBaseUrl(apiBaseUrl) || DEFAULT_API_BASE_URL;
    const redirectUri = browser.identity.getRedirectURL('cantaro-auth');
    const clientId = browser.runtime.id;
    const state = createRandomBase64Url(24);
    const codeVerifier = createRandomBase64Url(48);
    const codeChallenge = await createPkceChallenge(codeVerifier);
    const authorizeUrl = new URL(`${normalizedApiBaseUrl}/api/auth/extension/authorize`);

    authorizeUrl.searchParams.set('response_type', 'code');
    authorizeUrl.searchParams.set('client_id', clientId);
    authorizeUrl.searchParams.set('redirect_uri', redirectUri);
    authorizeUrl.searchParams.set('state', state);
    authorizeUrl.searchParams.set('code_challenge', codeChallenge);
    authorizeUrl.searchParams.set('code_challenge_method', 'S256');

    const callbackUrl = await browser.identity.launchWebAuthFlow({
        interactive: true,
        url: authorizeUrl.toString(),
    });

    if (!callbackUrl) {
        throw new Error('The browser did not return an authorization callback URL.');
    }

    const callback = new URL(callbackUrl);
    const returnedState = callback.searchParams.get('state')?.trim() ?? '';
    const error = callback.searchParams.get('error')?.trim() ?? '';
    const errorDescription = callback.searchParams.get('error_description')?.trim() ?? '';
    const authorizationCode = callback.searchParams.get('code')?.trim() ?? '';

    if (error) {
        throw new Error(errorDescription || error || 'Extension sign-in was denied.');
    }

    if (!authorizationCode || returnedState !== state) {
        throw new Error('The authorization callback was invalid or did not match the original request.');
    }

    const tokenResponse = await postTokenGrant(normalizedApiBaseUrl, new URLSearchParams({
        grant_type: 'authorization_code',
        code: authorizationCode,
        client_id: clientId,
        redirect_uri: redirectUri,
        code_verifier: codeVerifier,
    }));

    return persistTokenExchange(normalizedApiBaseUrl, tokenResponse);
}

export async function ensureExtensionAccessToken(apiBaseUrl?: string, forceRefresh = false): Promise<string | null> {
    const config = await readExtensionConfig();
    const normalizedApiBaseUrl = normalizeApiBaseUrl(apiBaseUrl || config.apiBaseUrl) || DEFAULT_API_BASE_URL;
    const storedApiBaseUrl = normalizeApiBaseUrl(config.apiBaseUrl) || DEFAULT_API_BASE_URL;

    if (normalizedApiBaseUrl !== storedApiBaseUrl) {
        await clearExtensionSession(normalizedApiBaseUrl);
        return null;
    }

    if (!forceRefresh && isAccessTokenFresh(config)) {
        return config.accessToken;
    }

    if (!config.refreshToken) {
        return config.accessToken || null;
    }

    try {
        const tokenResponse = await postTokenGrant(normalizedApiBaseUrl, new URLSearchParams({
            grant_type: 'refresh_token',
            refresh_token: config.refreshToken,
            client_id: browser.runtime.id,
        }));

        const nextConfig = await persistTokenExchange(normalizedApiBaseUrl, tokenResponse);
        return nextConfig.accessToken;
    } catch {
        await clearExtensionSession(normalizedApiBaseUrl);
        return null;
    }
}

export async function resolveAuthenticatedExtensionConfig(apiBaseUrl?: string): Promise<ExtensionConfig> {
    const currentConfig = await readExtensionConfig();
    const normalizedApiBaseUrl = normalizeApiBaseUrl(apiBaseUrl || currentConfig.apiBaseUrl) || DEFAULT_API_BASE_URL;
    const accessToken = await ensureExtensionAccessToken(normalizedApiBaseUrl);
    const latestConfig = await readExtensionConfig();

    return {
        ...latestConfig,
        apiBaseUrl: normalizedApiBaseUrl,
        accessToken: accessToken ?? latestConfig.accessToken,
    };
}

export async function getVerifiedExtensionUser(apiBaseUrl?: string): Promise<ExtensionUser | null> {
    const config = await resolveAuthenticatedExtensionConfig(apiBaseUrl);
    if (!config.accessToken) {
        return null;
    }

    try {
        const user = await fetchCurrentUser(config.apiBaseUrl, config.accessToken);

        if (user.email !== config.sessionEmail) {
            await saveExtensionConfig({
                ...config,
                sessionEmail: user.email,
            });
        }

        return user;
    } catch {
        const refreshedAccessToken = await ensureExtensionAccessToken(config.apiBaseUrl, true);
        if (!refreshedAccessToken) {
            return null;
        }

        try {
            const user = await fetchCurrentUser(config.apiBaseUrl, refreshedAccessToken);
            await saveExtensionConfig({
                ...config,
                accessToken: refreshedAccessToken,
                sessionEmail: user.email,
            });

            return user;
        } catch {
            await clearExtensionSession(config.apiBaseUrl);
            return null;
        }
    }
}

export async function revokeExtensionSession(apiBaseUrl?: string): Promise<void> {
    const config = await readExtensionConfig();
    const normalizedApiBaseUrl = normalizeApiBaseUrl(apiBaseUrl || config.apiBaseUrl) || DEFAULT_API_BASE_URL;

    if (config.refreshToken) {
        await fetch(`${normalizedApiBaseUrl}/api/auth/extension/revoke`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            },
            body: new URLSearchParams({
                client_id: browser.runtime.id,
                refresh_token: config.refreshToken,
            }).toString(),
        }).catch(() => null);
    }

    await clearExtensionSession(normalizedApiBaseUrl);
}