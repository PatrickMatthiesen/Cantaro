import type { PlatformId } from '../types';

export interface PlatformApiErrorResponse {
    code?: string;
    error?: string;
    detail?: string;
    title?: string;
    retryAfterSeconds?: number;
}

export class PlatformApiError extends Error {
    public readonly platformId: PlatformId;
    public readonly status: number;
    public readonly code?: string;
    public readonly retryAfterSeconds?: number;

    public constructor(
        platformId: PlatformId,
        message: string,
        status: number,
        code?: string,
        retryAfterSeconds?: number,
    ) {
        super(message);
        this.name = 'PlatformApiError';
        this.platformId = platformId;
        this.status = status;
        this.code = code;
        this.retryAfterSeconds = retryAfterSeconds;
    }
}

export class PlatformReconnectRequiredError extends PlatformApiError {
    public constructor(platformId: PlatformId, message: string, code?: string) {
        super(platformId, message, 409, code);
        this.name = 'PlatformReconnectRequiredError';
    }
}

export function isPlatformReconnectRequiredError(error: unknown): error is PlatformReconnectRequiredError {
    return error instanceof PlatformReconnectRequiredError;
}

function isReconnectCode(platformId: PlatformId, code?: string) {
    return code === 'platform_reconnect_required'
        || code === `${platformId}_reconnect_required`
        || code?.endsWith('_reconnect_required') === true;
}

function readRetryAfterSeconds(response: Response, body: PlatformApiErrorResponse) {
    if (body.retryAfterSeconds !== undefined) {
        return body.retryAfterSeconds;
    }

    const retryAfterHeader = Number.parseInt(response.headers.get('Retry-After') ?? '', 10);
    return Number.isFinite(retryAfterHeader) ? retryAfterHeader : undefined;
}

function fallbackForStatus(platformId: PlatformId, response: Response, fallbackMessage: string) {
    if (response.status === 401) {
        return 'Your Cantaro session expired. Sign in again, then retry.';
    }
    if (response.status === 403) {
        const platformName = platformId === 'spotify' ? 'Spotify' : 'The music platform';
        return `${platformName} denied this request. Reconnect the account if the permission changed.`;
    }
    if (response.status === 429) {
        return 'The music platform is rate limiting requests. Wait a moment, then retry.';
    }
    return fallbackMessage;
}

export async function createPlatformApiError(
    platformId: PlatformId,
    response: Response,
    fallbackMessage: string,
): Promise<PlatformApiError> {
    const body = await response.json().catch((): PlatformApiErrorResponse => ({})) as PlatformApiErrorResponse;
    const message = body.error || body.detail || body.title || fallbackForStatus(platformId, response, fallbackMessage);

    if (response.status === 409 && isReconnectCode(platformId, body.code)) {
        return new PlatformReconnectRequiredError(platformId, message, body.code);
    }

    return new PlatformApiError(
        platformId,
        message,
        response.status,
        body.code,
        readRetryAfterSeconds(response, body),
    );
}
