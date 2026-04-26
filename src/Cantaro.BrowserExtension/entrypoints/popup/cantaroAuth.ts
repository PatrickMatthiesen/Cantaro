import { normalizeApiBaseUrl } from '../../lib/extensionRuntimeConfig';

export interface ExtensionUser {
    email: string;
}

export interface TokenLoginResult {
    accessToken: string;
}

function readErrorMessage(payload: unknown, fallbackMessage: string): string {
    if (!payload || typeof payload !== 'object') {
        return fallbackMessage;
    }

    const record = payload as Record<string, unknown>;
    const candidates = [record.message, record.error, record.detail, record.title];
    const firstMessage = candidates.find((value) => typeof value === 'string' && value.trim());
    return typeof firstMessage === 'string' ? firstMessage : fallbackMessage;
}

function readTokenValue(record: Record<string, unknown>, keys: string[]): string {
    for (const key of keys) {
        const value = record[key];
        if (typeof value === 'string' && value.trim()) {
            return value.trim();
        }
    }

    return '';
}

function parseTokenLoginResult(payload: unknown): TokenLoginResult {
    if (!payload || typeof payload !== 'object') {
        throw new Error('Cantaro API did not return a bearer token.');
    }

    const record = payload as Record<string, unknown>;
    const accessToken = readTokenValue(record, ['accessToken', 'token', 'access_token']);

    if (!accessToken) {
        throw new Error('Cantaro API did not return a bearer token.');
    }

    return {
        accessToken,
    };
}

export async function fetchCurrentUser(apiBaseUrl: string, accessToken: string): Promise<ExtensionUser> {
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

export async function signInToCantaro(apiBaseUrl: string, email: string, password: string): Promise<TokenLoginResult> {
    const response = await fetch(`${normalizeApiBaseUrl(apiBaseUrl)}/api/login?useCookies=false`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({ email, password }),
    });

    const payload = await response.json().catch(() => null);
    if (!response.ok) {
        throw new Error(readErrorMessage(payload, 'Login failed.'));
    }

    return parseTokenLoginResult(payload);
}