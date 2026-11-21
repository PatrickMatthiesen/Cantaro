export type MediaApiErrorKind =
    | 'connection'
    | 'authentication'
    | 'authorization'
    | 'server'
    | 'service-unavailable'
    | 'request';

export interface MediaApiErrorOptions {
    status?: number;
    code?: string;
    responseBody?: unknown;
    retryable?: boolean;
    cause?: unknown;
}

export class MediaApiError extends Error {
    public readonly kind: MediaApiErrorKind;
    public readonly status?: number;
    public readonly code?: string;
    public readonly responseBody?: unknown;
    public readonly retryable: boolean;

    public constructor(kind: MediaApiErrorKind, message: string, options: MediaApiErrorOptions = {}) {
        super(message);
        this.name = 'MediaApiError';
        this.kind = kind;
        this.status = options.status;
        this.code = options.code;
        this.responseBody = options.responseBody;
        this.retryable = options.retryable ?? kind !== 'authorization';
        if (options.cause !== undefined) {
            this.cause = options.cause;
        }
    }
}

export function isMediaApiError(error: unknown): error is MediaApiError {
    return error instanceof MediaApiError;
}

function statusOf(error: unknown): number | undefined {
    if (!error || typeof error !== 'object' || !('status' in error)) {
        return undefined;
    }

    const status = (error as { status?: unknown }).status;
    return typeof status === 'number' ? status : undefined;
}

type StatusError = { kind: MediaApiErrorKind; message: string };

const serviceUnavailable: StatusError = {
    kind: 'service-unavailable',
    message: 'The Cantaro server is temporarily unavailable. Try again in a moment.',
};

const statusErrors = new Map<number, StatusError>([
    [401, { kind: 'authentication', message: 'Your Cantaro session has expired. Sign in again, then try again.' }],
    [403, { kind: 'authorization', message: 'Cantaro cannot access this request with your current permissions.' }],
    [429, { kind: 'request', message: 'Too many requests. Try again in a moment.' }],
    [502, serviceUnavailable],
    [503, serviceUnavailable],
    [504, serviceUnavailable],
]);

function describeStatus(status: number, fallbackMessage: string): StatusError {
    return statusErrors.get(status) ?? (status >= 500
        ? { kind: 'server', message: 'The Cantaro server could not complete this request. Try again in a moment.' }
        : { kind: 'request', message: fallbackMessage });
}

export function mediaApiErrorFromStatus(
    status: number,
    fallbackMessage: string,
    options: Omit<MediaApiErrorOptions, 'status'> = {},
): MediaApiError {
    const { kind, message } = describeStatus(status, fallbackMessage);
    return new MediaApiError(kind, message, {
        ...options,
        status,
        retryable: options.retryable ?? (kind !== 'authorization' && status !== 401),
    });
}

export function createMediaApiConnectionError(cause?: unknown): MediaApiError {
    return new MediaApiError(
        'connection',
        "Can't connect to the Cantaro server. Check your connection and try again.",
        { retryable: true, cause },
    );
}

export function toMediaApiError(error: unknown, fallbackMessage = 'Cantaro could not complete this request.'): MediaApiError {
    if (isMediaApiError(error)) {
        return error;
    }

    const status = statusOf(error);
    if (status !== undefined) {
        return mediaApiErrorFromStatus(status, fallbackMessage, {
            responseBody: error && typeof error === 'object' && 'responseBody' in error
                ? (error as { responseBody?: unknown }).responseBody
                : undefined,
            cause: error,
        });
    }

    return new MediaApiError('request', fallbackMessage, { retryable: true, cause: error });
}
